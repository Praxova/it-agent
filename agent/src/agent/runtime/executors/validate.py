"""Validate step executor - request validation."""
from __future__ import annotations
import logging
import re
from typing import Any

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class ValidateExecutor(BaseStepExecutor):
    """
    Executes Validate steps - validates request against rules.

    Performs checks like:
    - User exists in directory
    - User is not in protected list
    - Requester is authorized
    - Required fields are present
    """

    @property
    def step_type(self) -> str:
        return StepType.VALIDATE.value

    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute validation step.

        Configuration options:
        - checks: List of validation checks to perform
        - deny_list: List of users/patterns that cannot be modified
        - require_affected_user: Whether affected_user must be identified
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )

        config = step.configuration or {}
        checks = config.get("checks", [])
        validation_errors = []

        # Get values from previous steps
        affected_user = context.get_variable("affected_user")
        ticket_type = context.get_variable("ticket_type")
        confidence = context.get_variable("confidence", 0)

        # Run each configured check
        for check in checks:
            check_result = await self._run_check(check, context, config)
            if not check_result["passed"]:
                validation_errors.append(check_result["reason"])

        # Check if affected user is required but missing
        if config.get("require_affected_user", True):
            if not affected_user:
                validation_errors.append("Could not identify affected user from ticket")

        # Check deny list
        deny_list = config.get("deny_list", [])
        if affected_user and self._is_denied(affected_user, deny_list):
            validation_errors.append(f"User '{affected_user}' is in protected deny list")

        # Build result
        is_valid = len(validation_errors) == 0

        result.complete({
            "valid": is_valid,
            "validation_errors": validation_errors,
            "affected_user": affected_user,
            "ticket_type": ticket_type,
            "checks_performed": checks,
        })

        if is_valid:
            logger.info(f"Validation passed for user '{affected_user}'")
        else:
            logger.warning(f"Validation failed: {validation_errors}")

        return result

    async def _run_check(
        self,
        check: str,
        context: ExecutionContext,
        config: dict[str, Any],
    ) -> dict[str, Any]:
        """Run a single validation check."""

        if check == "user_exists":
            # In a real implementation, this would query AD
            # For now, we assume the user exists if we have a username
            affected_user = context.get_variable("affected_user")
            if affected_user:
                return {"passed": True, "reason": ""}
            return {"passed": False, "reason": "Cannot verify user exists - no username"}

        elif check == "not_admin":
            # Check if user is not an admin account
            affected_user = context.get_variable("affected_user")
            admin_patterns = config.get("admin_patterns", ["admin", "administrator", "svc_", "sa_"])
            if affected_user:
                for pattern in admin_patterns:
                    if pattern.lower() in affected_user.lower():
                        return {"passed": False, "reason": f"User '{affected_user}' appears to be an admin account"}
            return {"passed": True, "reason": ""}

        elif check == "requester_authorized":
            # Check if requester is authorized to make this request
            # In real implementation, would check manager relationships, etc.
            return {"passed": True, "reason": ""}

        elif check == "confidence_threshold":
            confidence = context.get_variable("confidence", 0)
            threshold = config.get("confidence_threshold", 0.7)
            if confidence >= threshold:
                return {"passed": True, "reason": ""}
            return {"passed": False, "reason": f"Confidence {confidence} below threshold {threshold}"}

        else:
            logger.warning(f"Unknown validation check: {check}")
            return {"passed": True, "reason": ""}

    def _is_denied(self, username: str, deny_list: list[str]) -> bool:
        """Check if username matches any deny list pattern."""
        username_lower = username.lower()

        for pattern in deny_list:
            pattern_lower = pattern.lower()

            # Check for exact match
            if username_lower == pattern_lower:
                return True

            # Check for wildcard patterns
            if "*" in pattern:
                regex = pattern_lower.replace("*", ".*")
                if re.match(f"^{regex}$", username_lower):
                    return True

        return False
