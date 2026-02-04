# Claude Code Prompt: Phase 4B-2 - Step Executors

## Context

Phase 4B-1 created the core workflow runtime infrastructure:
- `ConfigLoader` - Fetches export from Admin Portal
- `WorkflowEngine` - Executes workflows, follows transitions
- `ExecutionContext` - Shared state during execution
- `ConditionEvaluator` - Evaluates transition conditions
- `BaseStepExecutor` - Abstract base for executors

Now we implement the actual step executors that perform work for each step type.

**Project Location**: `/home/alton/Documents/lucid-it-agent`
**Runtime Location**: `/home/alton/Documents/lucid-it-agent/agent/src/agent/runtime`

## Overview

Implement executors for all step types. Each executor:
1. Receives step definition, execution context, and rulesets
2. Performs the step's action (LLM call, Tool Server call, etc.)
3. Returns StepResult with output data for condition evaluation

## File Structure

Create these files under `agent/src/agent/runtime/executors/`:
````
executors/
├── __init__.py           # UPDATE - export all executors
├── base.py               # EXISTS - BaseStepExecutor
├── trigger.py            # NEW - TriggerExecutor
├── classify.py           # NEW - ClassifyExecutor (LLM)
├── query.py              # NEW - QueryExecutor
├── validate.py           # NEW - ValidateExecutor
├── execute.py            # NEW - ExecuteExecutor (Tool Server)
├── update_ticket.py      # NEW - UpdateTicketExecutor
├── notify.py             # NEW - NotifyExecutor
├── escalate.py           # NEW - EscalateExecutor
├── end.py                # NEW - EndExecutor
└── registry.py           # NEW - ExecutorRegistry
````

## Task 1: Create TriggerExecutor (trigger.py)

The trigger step initializes the workflow - validates the ticket exists and sets up initial context.
````python
"""Trigger step executor - workflow entry point."""
from __future__ import annotations
import logging
from datetime import datetime

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class TriggerExecutor(BaseStepExecutor):
    """
    Executes Trigger steps - the entry point of a workflow.
    
    Validates that required ticket data is present and initializes
    workflow variables from step configuration.
    """
    
    @property
    def step_type(self) -> str:
        return StepType.TRIGGER.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute trigger step.
        
        Configuration options:
        - required_fields: List of ticket fields that must be present
        - init_variables: Dict of variables to initialize in context
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        config = step.configuration or {}
        
        # Validate required fields
        required_fields = config.get("required_fields", ["short_description"])
        missing_fields = []
        
        for field in required_fields:
            if field not in context.ticket_data or not context.ticket_data[field]:
                missing_fields.append(field)
        
        if missing_fields:
            result.fail(f"Missing required ticket fields: {', '.join(missing_fields)}")
            return result
        
        # Initialize variables from config
        init_vars = config.get("init_variables", {})
        for key, value in init_vars.items():
            context.set_variable(key, value)
        
        # Extract common fields to variables for easy access
        context.set_variable("ticket_id", context.ticket_id)
        context.set_variable("short_description", context.ticket_data.get("short_description", ""))
        context.set_variable("description", context.ticket_data.get("description", ""))
        context.set_variable("caller", context.ticket_data.get("caller_id", ""))
        context.set_variable("triggered_at", datetime.utcnow().isoformat())
        
        logger.info(f"Trigger activated for ticket {context.ticket_id}")
        
        result.complete({
            "triggered": True,
            "ticket_id": context.ticket_id,
            "source": config.get("source", "servicenow"),
        })
        
        return result
````

## Task 2: Create ClassifyExecutor (classify.py)

The most complex executor - uses LLM with rulesets and examples for ticket classification.
````python
"""Classify step executor - LLM-powered ticket classification."""
from __future__ import annotations
import json
import logging
import re
from typing import Any

from griptape.structures import Agent
from griptape.rules import Rule, Ruleset

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType, ExampleSetExportInfo
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class ClassifyExecutor(BaseStepExecutor):
    """
    Executes Classify steps - uses LLM to classify tickets.
    
    Builds prompts with:
    - Rulesets (classification rules, security rules)
    - Few-shot examples from example sets
    - Ticket data to classify
    """
    
    @property
    def step_type(self) -> str:
        return StepType.CLASSIFY.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute classification step.
        
        Configuration options:
        - use_example_set: Name of example set to use for few-shot
        - output_format: Expected output fields
        - max_retries: Number of retries on parse failure
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        if not context.llm_driver:
            result.fail("No LLM driver configured")
            return result
        
        config = step.configuration or {}
        
        # Get applicable rulesets
        step_rulesets = self.get_step_rulesets(step, rulesets)
        
        # Build the classification prompt
        prompt = self._build_classification_prompt(
            ticket_data=context.ticket_data,
            rulesets=step_rulesets,
            example_set_name=config.get("use_example_set"),
            context=context,
        )
        
        # Create Griptape agent with rules
        griptape_rules = self._build_griptape_rules(step_rulesets)
        
        try:
            agent = Agent(
                prompt_driver=context.llm_driver,
                rules=griptape_rules,
            )
            
            # Run classification
            logger.debug(f"Classification prompt:\n{prompt[:500]}...")
            response = agent.run(prompt)
            response_text = response.output_task.output.value
            
            logger.debug(f"Classification response:\n{response_text}")
            
            # Parse the response
            classification = self._parse_classification_response(response_text)
            
            # Store results in context for condition evaluation
            for key, value in classification.items():
                context.set_variable(key, value)
            
            result.complete(classification)
            logger.info(f"Classification result: {classification.get('ticket_type')} "
                       f"(confidence: {classification.get('confidence', 'N/A')})")
            
        except Exception as e:
            logger.error(f"Classification failed: {e}")
            result.fail(str(e))
        
        return result
    
    def _build_classification_prompt(
        self,
        ticket_data: dict[str, Any],
        rulesets: list[RulesetExportInfo],
        example_set_name: str | None,
        context: ExecutionContext,
    ) -> str:
        """Build the classification prompt with rules and examples."""
        parts = []
        
        # System instruction
        parts.append("""You are an IT helpdesk ticket classifier. Analyze the ticket and classify it.

Your response MUST be valid JSON with these fields:
- ticket_type: One of "password_reset", "group_access_add", "group_access_remove", "file_permission", "unknown"
- confidence: A number between 0.0 and 1.0 indicating your confidence
- affected_user: The username of the person the request is about (if identifiable)
- target_group: The AD group name (if this is a group access request)
- target_resource: The file/folder path (if this is a permission request)
- reasoning: Brief explanation of your classification

Respond with ONLY the JSON object, no other text.""")
        
        # Add rules from rulesets
        rules_text = self.build_rules_prompt(rulesets)
        if rules_text:
            parts.append(rules_text)
        
        # Add few-shot examples if available
        # Note: Example sets would come from context.export.example_sets
        # For now, we'll use inline examples
        parts.append("""
## Examples

Example 1:
Ticket: "User jsmith forgot their password and needs it reset"
Classification:
```json
{"ticket_type": "password_reset", "confidence": 0.95, "affected_user": "jsmith", "target_group": null, "target_resource": null, "reasoning": "Clear password reset request with username identified"}
```

Example 2:
Ticket: "Please add Mary Johnson (mjohnson) to the Finance-ReadOnly group"
Classification:
```json
{"ticket_type": "group_access_add", "confidence": 0.92, "affected_user": "mjohnson", "target_group": "Finance-ReadOnly", "target_resource": null, "reasoning": "Request to add user to AD group"}
```

Example 3:
Ticket: "I need access to the Q4 reports folder at \\\\fileserver\\finance\\Q4"
Classification:
```json
{"ticket_type": "file_permission", "confidence": 0.88, "affected_user": null, "target_group": null, "target_resource": "\\\\fileserver\\finance\\Q4", "reasoning": "File permission request, user not explicitly named"}
```
""")
        
        # Add the ticket to classify
        short_desc = ticket_data.get("short_description", "")
        description = ticket_data.get("description", "")
        caller = ticket_data.get("caller_id", "Unknown")
        
        parts.append(f"""
## Ticket to Classify

**Caller**: {caller}
**Short Description**: {short_desc}
**Description**: {description}

Classify this ticket and respond with ONLY the JSON object:""")
        
        return "\n\n".join(parts)
    
    def _build_griptape_rules(self, rulesets: list[RulesetExportInfo]) -> list[Rule]:
        """Convert rulesets to Griptape Rule objects."""
        rules = []
        
        for ruleset in rulesets:
            for rule in ruleset.rules:
                if rule.is_enabled:
                    rules.append(Rule(rule.rule_text))
        
        return rules
    
    def _parse_classification_response(self, response: str) -> dict[str, Any]:
        """Parse LLM response to extract classification JSON."""
        # Try to extract JSON from response
        # Handle cases where LLM wraps in markdown code blocks
        
        # Remove markdown code blocks if present
        json_match = re.search(r'```(?:json)?\s*([\s\S]*?)\s*```', response)
        if json_match:
            json_str = json_match.group(1)
        else:
            # Try to find raw JSON object
            json_match = re.search(r'\{[\s\S]*\}', response)
            if json_match:
                json_str = json_match.group(0)
            else:
                # Return low confidence unknown if we can't parse
                logger.warning(f"Could not parse classification response: {response[:200]}")
                return {
                    "ticket_type": "unknown",
                    "confidence": 0.3,
                    "reasoning": "Failed to parse LLM response",
                    "affected_user": None,
                    "target_group": None,
                    "target_resource": None,
                }
        
        try:
            classification = json.loads(json_str)
            
            # Ensure required fields with defaults
            classification.setdefault("ticket_type", "unknown")
            classification.setdefault("confidence", 0.5)
            classification.setdefault("affected_user", None)
            classification.setdefault("target_group", None)
            classification.setdefault("target_resource", None)
            classification.setdefault("reasoning", "")
            
            # Normalize confidence to float
            classification["confidence"] = float(classification["confidence"])
            
            return classification
            
        except json.JSONDecodeError as e:
            logger.warning(f"JSON parse error: {e}")
            return {
                "ticket_type": "unknown",
                "confidence": 0.3,
                "reasoning": f"JSON parse error: {e}",
                "affected_user": None,
                "target_group": None,
                "target_resource": None,
            }
````

## Task 3: Create ValidateExecutor (validate.py)

Validates the request against rules and checks.
````python
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
````

## Task 4: Create ExecuteExecutor (execute.py)

Calls Tool Server via capability routing.
````python
"""Execute step executor - calls Tool Server."""
from __future__ import annotations
import logging
from typing import Any

import httpx

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class ExecuteExecutor(BaseStepExecutor):
    """
    Executes Execute steps - calls Tool Server APIs.
    
    Uses capability routing to find the appropriate Tool Server,
    then calls its API to perform the action.
    """
    
    @property
    def step_type(self) -> str:
        return StepType.EXECUTE.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute tool server action.
        
        Configuration options:
        - capability: The capability to invoke (e.g., "ad-password-reset")
        - action_params: Static parameters for the action
        - param_mapping: Map context variables to action parameters
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        config = step.configuration or {}
        capability = config.get("capability")
        
        if not capability:
            result.fail("No capability specified in step configuration")
            return result
        
        try:
            # Get Tool Server URL for this capability
            tool_server_url = await self._get_tool_server_url(capability, context)
            
            if not tool_server_url:
                result.fail(f"No Tool Server found for capability: {capability}")
                return result
            
            # Build action parameters
            params = self._build_action_params(config, context)
            
            # Execute the action
            action_result = await self._call_tool_server(
                tool_server_url=tool_server_url,
                capability=capability,
                params=params,
            )
            
            result.complete({
                "success": action_result.get("success", False),
                "message": action_result.get("message", ""),
                "capability": capability,
                "tool_server": tool_server_url,
                "action_result": action_result,
            })
            
            if action_result.get("success"):
                logger.info(f"Action '{capability}' succeeded: {action_result.get('message')}")
            else:
                logger.warning(f"Action '{capability}' failed: {action_result.get('message')}")
            
        except Exception as e:
            logger.error(f"Execute step failed: {e}")
            result.fail(str(e))
        
        return result
    
    async def _get_tool_server_url(
        self,
        capability: str,
        context: ExecutionContext,
    ) -> str | None:
        """Query Admin Portal for Tool Server URL."""
        if not context.admin_portal_url:
            logger.warning("No Admin Portal URL configured, using mock")
            return "http://localhost:8080"  # Default for testing
        
        url = f"{context.admin_portal_url}/api/capabilities/{capability}/servers"
        
        try:
            async with httpx.AsyncClient() as client:
                response = await client.get(url, timeout=10.0)
                
                if response.status_code == 404:
                    return None
                
                response.raise_for_status()
                servers = response.json()
                
                if servers and len(servers) > 0:
                    # Return first healthy server
                    return servers[0].get("url")
                
                return None
                
        except Exception as e:
            logger.error(f"Failed to query capability routing: {e}")
            return None
    
    def _build_action_params(
        self,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> dict[str, Any]:
        """Build action parameters from config and context."""
        params = dict(config.get("action_params", {}))
        
        # Map context variables to parameters
        param_mapping = config.get("param_mapping", {
            "username": "affected_user",
            "user": "affected_user",
            "group_name": "target_group",
            "path": "target_resource",
        })
        
        for param_name, var_name in param_mapping.items():
            value = context.get_variable(var_name)
            if value is not None:
                params[param_name] = value
        
        return params
    
    async def _call_tool_server(
        self,
        tool_server_url: str,
        capability: str,
        params: dict[str, Any],
    ) -> dict[str, Any]:
        """Call the Tool Server API."""
        # Map capability to endpoint
        endpoint_map = {
            "ad-password-reset": "/api/v1/password/reset",
            "ad-group-add": "/api/v1/groups/add-member",
            "ad-group-remove": "/api/v1/groups/remove-member",
            "ntfs-permission-grant": "/api/v1/permissions/grant",
            "ntfs-permission-revoke": "/api/v1/permissions/revoke",
        }
        
        endpoint = endpoint_map.get(capability, f"/api/v1/{capability}")
        url = f"{tool_server_url.rstrip('/')}{endpoint}"
        
        logger.info(f"Calling Tool Server: POST {url}")
        logger.debug(f"Parameters: {params}")
        
        try:
            async with httpx.AsyncClient() as client:
                response = await client.post(
                    url,
                    json=params,
                    timeout=30.0,
                )
                
                if response.status_code >= 400:
                    return {
                        "success": False,
                        "message": f"Tool Server returned {response.status_code}: {response.text}",
                    }
                
                return response.json()
                
        except httpx.TimeoutException:
            return {"success": False, "message": "Tool Server request timed out"}
        except Exception as e:
            return {"success": False, "message": str(e)}
````

## Task 5: Create NotifyExecutor (notify.py)

Sends notifications via ticket comments or other channels.
````python
"""Notify step executor - sends notifications."""
from __future__ import annotations
import logging
from typing import Any

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class NotifyExecutor(BaseStepExecutor):
    """
    Executes Notify steps - sends notifications.
    
    Supports channels:
    - ticket-comment: Add comment to ServiceNow ticket
    - email: Send email notification
    - teams: Send Teams message
    """
    
    @property
    def step_type(self) -> str:
        return StepType.NOTIFY.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute notification step.
        
        Configuration options:
        - channel: "ticket-comment", "email", "teams"
        - template: Template name or inline template
        - include_temp_password: Whether to include temp password in message
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        config = step.configuration or {}
        channel = config.get("channel", "ticket-comment")
        template = config.get("template", "default")
        
        try:
            # Build the notification message
            message = self._build_message(template, config, context)
            
            # Send via appropriate channel
            if channel == "ticket-comment":
                await self._add_ticket_comment(context, message, config)
            elif channel == "email":
                await self._send_email(context, message, config)
            elif channel == "teams":
                await self._send_teams_message(context, message, config)
            else:
                logger.warning(f"Unknown notification channel: {channel}")
            
            result.complete({
                "notified": True,
                "channel": channel,
                "message_preview": message[:200] if len(message) > 200 else message,
            })
            
            logger.info(f"Notification sent via {channel}")
            
        except Exception as e:
            logger.error(f"Notification failed: {e}")
            result.fail(str(e))
        
        return result
    
    def _build_message(
        self,
        template: str,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> str:
        """Build notification message from template."""
        # Get action result from previous execute step
        execute_result = context.get_step_output("execute-reset", "action_result", {})
        affected_user = context.get_variable("affected_user", "the user")
        ticket_type = context.get_variable("ticket_type", "request")
        
        # Template selection
        if template == "password-reset-success":
            temp_password = execute_result.get("temp_password", "[provided securely]")
            include_password = config.get("include_temp_password", False)
            
            if include_password:
                return f"""Your password reset request has been completed.

User: {affected_user}
Temporary Password: {temp_password}

Please log in and change your password immediately.

This action was performed automatically by the IT helpdesk system."""
            else:
                return f"""Your password reset request has been completed.

User: {affected_user}

The temporary password has been sent via a secure channel.
Please log in and change your password immediately.

This action was performed automatically by the IT helpdesk system."""
        
        elif template == "group-access-granted":
            target_group = context.get_variable("target_group", "the requested group")
            return f"""Group access has been granted.

User: {affected_user}
Group: {target_group}

This action was performed automatically by the IT helpdesk system."""
        
        elif template == "escalation":
            reason = context.escalation_reason or "Manual review required"
            return f"""This ticket has been escalated to a human operator.

Reason: {reason}

A technician will review your request shortly."""
        
        else:
            # Default template
            return f"""Your {ticket_type} request has been processed.

This action was performed automatically by the IT helpdesk system.
If you have questions, please reply to this ticket."""
    
    async def _add_ticket_comment(
        self,
        context: ExecutionContext,
        message: str,
        config: dict[str, Any],
    ) -> None:
        """Add comment to ServiceNow ticket."""
        # In real implementation, would call ServiceNow API
        # For now, just log
        logger.info(f"Would add ticket comment to {context.ticket_id}:\n{message}")
        
        # Store for later use
        context.set_variable("last_notification", message)
    
    async def _send_email(
        self,
        context: ExecutionContext,
        message: str,
        config: dict[str, Any],
    ) -> None:
        """Send email notification."""
        recipient = config.get("recipient") or context.ticket_data.get("caller_email")
        logger.info(f"Would send email to {recipient}:\n{message}")
    
    async def _send_teams_message(
        self,
        context: ExecutionContext,
        message: str,
        config: dict[str, Any],
    ) -> None:
        """Send Teams notification."""
        channel = config.get("teams_channel", "IT-Notifications")
        logger.info(f"Would send Teams message to {channel}:\n{message}")
````

## Task 6: Create UpdateTicketExecutor (update_ticket.py)

Updates ServiceNow ticket state.
````python
"""UpdateTicket step executor - updates ticket in ServiceNow."""
from __future__ import annotations
import logging
from typing import Any

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class UpdateTicketExecutor(BaseStepExecutor):
    """
    Executes UpdateTicket steps - updates ServiceNow ticket.
    
    Can update:
    - State (in progress, resolved, closed)
    - Assignment
    - Work notes
    - Resolution details
    """
    
    @property
    def step_type(self) -> str:
        return StepType.UPDATE_TICKET.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute ticket update step.
        
        Configuration options:
        - state: Target state ("in_progress", "resolved", "closed")
        - close_code: Resolution code
        - add_resolution_notes: Whether to add resolution notes
        - work_notes: Static work notes to add
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        config = step.configuration or {}
        target_state = config.get("state", "resolved")
        
        try:
            update_payload = self._build_update_payload(config, context)
            
            # In real implementation, would call ServiceNow API
            await self._update_servicenow_ticket(context, update_payload)
            
            result.complete({
                "updated": True,
                "new_state": target_state,
                "ticket_id": context.ticket_id,
            })
            
            logger.info(f"Ticket {context.ticket_id} updated to state: {target_state}")
            
        except Exception as e:
            logger.error(f"Ticket update failed: {e}")
            result.fail(str(e))
        
        return result
    
    def _build_update_payload(
        self,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> dict[str, Any]:
        """Build ServiceNow update payload."""
        payload = {}
        
        # Map state names to ServiceNow values
        state_map = {
            "in_progress": "2",
            "on_hold": "3",
            "resolved": "6",
            "closed": "7",
        }
        
        state = config.get("state", "resolved")
        payload["state"] = state_map.get(state, "6")
        
        # Add close/resolution info
        if state in ("resolved", "closed"):
            close_code = config.get("close_code", "automated")
            payload["close_code"] = close_code
            
            if config.get("add_resolution_notes", True):
                # Build resolution notes from execution context
                ticket_type = context.get_variable("ticket_type", "request")
                affected_user = context.get_variable("affected_user", "N/A")
                
                resolution = f"Automated resolution by Lucid IT Agent\n"
                resolution += f"Ticket Type: {ticket_type}\n"
                resolution += f"Affected User: {affected_user}\n"
                
                # Add any step-specific notes
                execute_result = context.get_step_output("execute-reset", "message")
                if execute_result:
                    resolution += f"Action Result: {execute_result}\n"
                
                payload["close_notes"] = resolution
        
        # Add work notes if configured
        work_notes = config.get("work_notes")
        if work_notes:
            payload["work_notes"] = work_notes
        
        return payload
    
    async def _update_servicenow_ticket(
        self,
        context: ExecutionContext,
        payload: dict[str, Any],
    ) -> None:
        """Call ServiceNow API to update ticket."""
        # In real implementation, would use ServiceNow connector
        logger.info(f"Would update ServiceNow ticket {context.ticket_id} with: {payload}")
        
        # Store update info in context
        context.set_variable("ticket_updated", True)
        context.set_variable("ticket_new_state", payload.get("state"))
````

## Task 7: Create EscalateExecutor (escalate.py)

Escalates to human operator.
````python
"""Escalate step executor - escalates to human."""
from __future__ import annotations
import logging
from typing import Any

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class EscalateExecutor(BaseStepExecutor):
    """
    Executes Escalate steps - escalates ticket to human operator.
    
    Assigns ticket to escalation group and adds context
    about why automation couldn't complete.
    """
    
    @property
    def step_type(self) -> str:
        return StepType.ESCALATE.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute escalation step.
        
        Configuration options:
        - target_group: Assignment group for escalation
        - preserve_work_notes: Add automation work notes
        - reason_template: Template for escalation reason
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        config = step.configuration or {}
        target_group = config.get("target_group", "Level 2 Support")
        
        try:
            # Build escalation reason
            reason = self._build_escalation_reason(config, context)
            
            # Update ticket for escalation
            await self._escalate_ticket(context, target_group, reason, config)
            
            # Mark context as escalated
            context.escalate(reason)
            
            result.complete({
                "escalated": True,
                "target_group": target_group,
                "reason": reason,
            })
            
            logger.info(f"Ticket {context.ticket_id} escalated to '{target_group}': {reason}")
            
        except Exception as e:
            logger.error(f"Escalation failed: {e}")
            result.fail(str(e))
        
        return result
    
    def _build_escalation_reason(
        self,
        config: dict[str, Any],
        context: ExecutionContext,
    ) -> str:
        """Build escalation reason from context."""
        reasons = []
        
        # Check classification confidence
        confidence = context.get_variable("confidence", 0)
        if confidence < 0.8:
            reasons.append(f"Low classification confidence ({confidence:.2f})")
        
        # Check validation errors
        validation_errors = context.get_step_output("validate-request", "validation_errors", [])
        if validation_errors:
            reasons.append(f"Validation failed: {', '.join(validation_errors)}")
        
        # Check execution errors
        for step_name, step_result in context.step_results.items():
            if step_result.status == ExecutionStatus.FAILED:
                reasons.append(f"Step '{step_name}' failed: {step_result.error}")
        
        # Check for unknown ticket type
        ticket_type = context.get_variable("ticket_type")
        if ticket_type == "unknown":
            reasons.append("Could not classify ticket type")
        
        if not reasons:
            reasons.append("Escalated by workflow rule")
        
        return "; ".join(reasons)
    
    async def _escalate_ticket(
        self,
        context: ExecutionContext,
        target_group: str,
        reason: str,
        config: dict[str, Any],
    ) -> None:
        """Update ServiceNow ticket for escalation."""
        # Build work notes with automation context
        work_notes = f"""Automated Processing Escalation
===================================
Reason: {reason}

Automation Context:
- Ticket Type: {context.get_variable('ticket_type', 'Unknown')}
- Confidence: {context.get_variable('confidence', 'N/A')}
- Affected User: {context.get_variable('affected_user', 'Not identified')}

Steps Executed:
"""
        for step_name, step_result in context.step_results.items():
            status = "✓" if step_result.status == ExecutionStatus.COMPLETED else "✗"
            work_notes += f"  {status} {step_name}: {step_result.status.value}\n"
        
        # In real implementation, would call ServiceNow API
        logger.info(f"Would escalate ticket {context.ticket_id}:\n"
                   f"  Target Group: {target_group}\n"
                   f"  Work Notes: {work_notes[:200]}...")
        
        context.set_variable("escalation_work_notes", work_notes)
````

## Task 8: Create EndExecutor and QueryExecutor (end.py, query.py)
````python
# end.py
"""End step executor - marks workflow complete."""
from __future__ import annotations
import logging

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class EndExecutor(BaseStepExecutor):
    """
    Executes End steps - terminal step of workflow.
    
    Marks the workflow as complete and performs any cleanup.
    """
    
    @property
    def step_type(self) -> str:
        return StepType.END.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """Execute end step."""
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        # Mark workflow complete
        context.complete()
        
        result.complete({
            "ended": True,
            "final_status": context.status.value,
            "steps_executed": len(context.step_results),
        })
        
        logger.info(f"Workflow completed for ticket {context.ticket_id}")
        
        return result
````
````python
# query.py
"""Query step executor - queries external systems."""
from __future__ import annotations
import logging
from typing import Any

import httpx

from ..models import WorkflowStepExportInfo, RulesetExportInfo, StepType
from ..execution_context import ExecutionContext, StepResult, ExecutionStatus
from .base import BaseStepExecutor

logger = logging.getLogger(__name__)


class QueryExecutor(BaseStepExecutor):
    """
    Executes Query steps - queries external systems for context.
    
    Can query:
    - Active Directory for user info
    - ServiceNow for related tickets
    - Other systems via configured endpoints
    """
    
    @property
    def step_type(self) -> str:
        return StepType.QUERY.value
    
    async def execute(
        self,
        step: WorkflowStepExportInfo,
        context: ExecutionContext,
        rulesets: dict[str, RulesetExportInfo],
    ) -> StepResult:
        """
        Execute query step.
        
        Configuration options:
        - query_type: "ad_user", "related_tickets", "custom"
        - endpoint: Custom endpoint URL
        - params: Query parameters
        - store_as: Variable name to store results
        """
        result = StepResult(
            step_name=step.name,
            step_type=self.step_type,
            status=ExecutionStatus.RUNNING,
        )
        
        config = step.configuration or {}
        query_type = config.get("query_type", "custom")
        store_as = config.get("store_as", "query_result")
        
        try:
            if query_type == "ad_user":
                query_result = await self._query_ad_user(context, config)
            elif query_type == "related_tickets":
                query_result = await self._query_related_tickets(context, config)
            else:
                query_result = await self._query_custom(context, config)
            
            # Store result in context
            context.set_variable(store_as, query_result)
            
            result.complete({
                "query_type": query_type,
                "results_count": len(query_result) if isinstance(query_result, list) else 1,
                "stored_as": store_as,
            })
            
            logger.info(f"Query '{query_type}' completed, stored as '{store_as}'")
            
        except Exception as e:
            logger.error(f"Query failed: {e}")
            result.fail(str(e))
        
        return result
    
    async def _query_ad_user(
        self,
        context: ExecutionContext,
        config: dict[str, Any],
    ) -> dict[str, Any]:
        """Query AD for user information."""
        affected_user = context.get_variable("affected_user")
        
        if not affected_user:
            return {"found": False, "error": "No username to query"}
        
        # In real implementation, would query Tool Server
        # For now, return mock data
        logger.info(f"Would query AD for user: {affected_user}")
        
        return {
            "found": True,
            "username": affected_user,
            "display_name": f"User {affected_user}",
            "email": f"{affected_user}@example.com",
            "department": "IT",
            "manager": "manager@example.com",
        }
    
    async def _query_related_tickets(
        self,
        context: ExecutionContext,
        config: dict[str, Any],
    ) -> list[dict[str, Any]]:
        """Query ServiceNow for related tickets."""
        affected_user = context.get_variable("affected_user")
        
        # In real implementation, would query ServiceNow
        logger.info(f"Would query related tickets for: {affected_user}")
        
        return []  # No related tickets in mock
    
    async def _query_custom(
        self,
        context: ExecutionContext,
        config: dict[str, Any],
    ) -> Any:
        """Execute custom query."""
        endpoint = config.get("endpoint")
        
        if not endpoint:
            return {"error": "No endpoint configured"}
        
        params = config.get("params", {})
        
        async with httpx.AsyncClient() as client:
            response = await client.get(endpoint, params=params, timeout=30.0)
            response.raise_for_status()
            return response.json()
````

## Task 9: Create ExecutorRegistry (registry.py)
````python
"""Registry for step executors."""
from __future__ import annotations
import logging

from .base import BaseStepExecutor
from .trigger import TriggerExecutor
from .classify import ClassifyExecutor
from .query import QueryExecutor
from .validate import ValidateExecutor
from .execute import ExecuteExecutor
from .update_ticket import UpdateTicketExecutor
from .notify import NotifyExecutor
from .escalate import EscalateExecutor
from .end import EndExecutor

logger = logging.getLogger(__name__)


class ExecutorRegistry:
    """
    Registry of step executors.
    
    Provides factory method to get executor for a step type,
    and registers all built-in executors.
    """
    
    def __init__(self):
        self._executors: dict[str, BaseStepExecutor] = {}
        self._register_builtins()
    
    def _register_builtins(self):
        """Register all built-in executors."""
        builtins = [
            TriggerExecutor(),
            ClassifyExecutor(),
            QueryExecutor(),
            ValidateExecutor(),
            ExecuteExecutor(),
            UpdateTicketExecutor(),
            NotifyExecutor(),
            EscalateExecutor(),
            EndExecutor(),
        ]
        
        for executor in builtins:
            self.register(executor)
    
    def register(self, executor: BaseStepExecutor):
        """Register a step executor."""
        self._executors[executor.step_type] = executor
        logger.debug(f"Registered executor for: {executor.step_type}")
    
    def get(self, step_type: str) -> BaseStepExecutor | None:
        """Get executor for a step type."""
        return self._executors.get(step_type)
    
    def get_all(self) -> dict[str, BaseStepExecutor]:
        """Get all registered executors."""
        return dict(self._executors)


# Global default registry
default_registry = ExecutorRegistry()
````

## Task 10: Update executors/__init__.py
````python
"""Step executors for workflow runtime."""
from .base import BaseStepExecutor, StepExecutionError
from .trigger import TriggerExecutor
from .classify import ClassifyExecutor
from .query import QueryExecutor
from .validate import ValidateExecutor
from .execute import ExecuteExecutor
from .update_ticket import UpdateTicketExecutor
from .notify import NotifyExecutor
from .escalate import EscalateExecutor
from .end import EndExecutor
from .registry import ExecutorRegistry, default_registry

__all__ = [
    "BaseStepExecutor",
    "StepExecutionError",
    "TriggerExecutor",
    "ClassifyExecutor",
    "QueryExecutor",
    "ValidateExecutor",
    "ExecuteExecutor",
    "UpdateTicketExecutor",
    "NotifyExecutor",
    "EscalateExecutor",
    "EndExecutor",
    "ExecutorRegistry",
    "default_registry",
]
````

## Task 11: Update WorkflowEngine to Use Registry

Update `workflow_engine.py` to use the executor registry:
````python
# Add to imports
from .executors.registry import default_registry

# Update __init__ method
def __init__(
    self,
    export: AgentExport,
    llm_driver: BasePromptDriver,
    admin_portal_url: str = "",
    executor_registry: ExecutorRegistry | None = None,
):
    # ... existing code ...
    
    # Use provided registry or default
    self._registry = executor_registry or default_registry
    
    # Register all executors from registry
    for step_type, executor in self._registry.get_all().items():
        self.register_executor(executor)
````

## Task 12: Create Tests

Create `agent/tests/runtime/test_executors.py`:
````python
"""Tests for step executors."""
import pytest
from unittest.mock import MagicMock, AsyncMock

from agent.runtime.execution_context import ExecutionContext, ExecutionStatus
from agent.runtime.models import WorkflowStepExportInfo, StepType
from agent.runtime.executors import (
    TriggerExecutor,
    ValidateExecutor,
    EscalateExecutor,
    EndExecutor,
)


@pytest.fixture
def basic_context():
    """Create a basic execution context."""
    return ExecutionContext(
        ticket_id="INC0001234",
        ticket_data={
            "short_description": "Password reset for jsmith",
            "description": "User forgot password",
            "caller_id": "requester@example.com",
        }
    )


@pytest.fixture
def trigger_step():
    """Create a trigger step."""
    return WorkflowStepExportInfo(
        name="trigger-start",
        step_type=StepType.TRIGGER,
        configuration={"source": "servicenow"},
        sort_order=1,
    )


class TestTriggerExecutor:
    @pytest.mark.asyncio
    async def test_trigger_succeeds_with_valid_ticket(self, basic_context, trigger_step):
        executor = TriggerExecutor()
        result = await executor.execute(trigger_step, basic_context, {})
        
        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["triggered"] is True
        assert basic_context.get_variable("ticket_id") == "INC0001234"
    
    @pytest.mark.asyncio
    async def test_trigger_fails_missing_required_field(self, trigger_step):
        context = ExecutionContext(
            ticket_id="INC0001234",
            ticket_data={}  # Missing short_description
        )
        
        executor = TriggerExecutor()
        result = await executor.execute(trigger_step, context, {})
        
        assert result.status == ExecutionStatus.FAILED
        assert "Missing required" in result.error


class TestValidateExecutor:
    @pytest.mark.asyncio
    async def test_validate_passes_with_valid_user(self, basic_context):
        basic_context.set_variable("affected_user", "jsmith")
        basic_context.set_variable("ticket_type", "password_reset")
        
        step = WorkflowStepExportInfo(
            name="validate-request",
            step_type=StepType.VALIDATE,
            configuration={"checks": ["user_exists", "not_admin"]},
            sort_order=3,
        )
        
        executor = ValidateExecutor()
        result = await executor.execute(step, basic_context, {})
        
        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["valid"] is True
    
    @pytest.mark.asyncio
    async def test_validate_fails_admin_user(self, basic_context):
        basic_context.set_variable("affected_user", "admin")
        
        step = WorkflowStepExportInfo(
            name="validate-request",
            step_type=StepType.VALIDATE,
            configuration={"checks": ["not_admin"]},
            sort_order=3,
        )
        
        executor = ValidateExecutor()
        result = await executor.execute(step, basic_context, {})
        
        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["valid"] is False
        assert any("admin" in e for e in result.output["validation_errors"])


class TestEscalateExecutor:
    @pytest.mark.asyncio
    async def test_escalate_sets_reason(self, basic_context):
        basic_context.set_variable("confidence", 0.5)
        
        step = WorkflowStepExportInfo(
            name="escalate-to-human",
            step_type=StepType.ESCALATE,
            configuration={"target_group": "Level 2 Support"},
            sort_order=6,
        )
        
        executor = EscalateExecutor()
        result = await executor.execute(step, basic_context, {})
        
        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["escalated"] is True
        assert basic_context.status == ExecutionStatus.ESCALATED


class TestEndExecutor:
    @pytest.mark.asyncio
    async def test_end_completes_workflow(self, basic_context):
        step = WorkflowStepExportInfo(
            name="end",
            step_type=StepType.END,
            configuration={},
            sort_order=10,
        )
        
        executor = EndExecutor()
        result = await executor.execute(step, basic_context, {})
        
        assert result.status == ExecutionStatus.COMPLETED
        assert result.output["ended"] is True
        assert basic_context.status == ExecutionStatus.COMPLETED
````

## Verification
````bash
cd /home/alton/Documents/lucid-it-agent/agent

# Install dependencies
pip install -e ".[dev]"

# Run all tests
pytest tests/runtime/ -v

# Run executor tests specifically
pytest tests/runtime/test_executors.py -v

# Verify imports
python -c "
from agent.runtime.executors import (
    TriggerExecutor, ClassifyExecutor, ValidateExecutor,
    ExecuteExecutor, NotifyExecutor, EscalateExecutor, EndExecutor,
    default_registry
)
print(f'Registered executors: {list(default_registry.get_all().keys())}')
print('All executors imported successfully!')
"
````

## Summary

Phase 4B-2 implements all step executors:

| Executor | Purpose | Key Integration |
|----------|---------|-----------------|
| TriggerExecutor | Initialize workflow | Validates ticket fields |
| ClassifyExecutor | LLM classification | Uses Griptape Agent with rules |
| QueryExecutor | Query external systems | AD user info, related tickets |
| ValidateExecutor | Validate request | Deny lists, admin checks |
| ExecuteExecutor | Call Tool Server | Capability routing, REST API |
| UpdateTicketExecutor | Update ServiceNow | State changes, resolution |
| NotifyExecutor | Send notifications | Ticket comments, email |
| EscalateExecutor | Escalate to human | Assignment, work notes |
| EndExecutor | Complete workflow | Cleanup, final status |

Next phase (4B-3) will wire up the runtime with existing ServiceNow connector and test end-to-end.
