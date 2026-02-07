"""Evaluates transition conditions."""
from __future__ import annotations
import operator
import re
import logging
from typing import Any

logger = logging.getLogger(__name__)


class ConditionEvaluationError(Exception):
    """Raised when condition evaluation fails."""
    pass


class ConditionEvaluator:
    """
    Evaluates transition conditions against execution context.

    Supports simple conditions like:
    - confidence >= 0.8
    - valid == true
    - success == false
    - ticket_type == 'password-reset'

    Supports 'or' compound conditions:
    - ticket_type == 'unknown' or confidence < 0.7

    Supports 'and' compound conditions:
    - outcome == 'approved' and ticket_type == 'password-reset'

    Does NOT support NOT for security.
    """

    # Supported operators
    OPERATORS = {
        "==": operator.eq,
        "!=": operator.ne,
        ">=": operator.ge,
        "<=": operator.le,
        ">": operator.gt,
        "<": operator.lt,
    }

    # Pattern: variable operator value
    # Examples: confidence >= 0.8, valid == true, type == "password_reset"
    CONDITION_PATTERN = re.compile(
        r'^\s*(\w+(?:\.\w+)?)\s*(==|!=|>=|<=|>|<)\s*(.+?)\s*$'
    )

    def evaluate(self, condition: str | None, context: dict[str, Any]) -> bool:
        """
        Evaluate a condition against the given context.

        Args:
            condition: Condition string (e.g., "confidence >= 0.8") or None.
                       Supports 'or' compound: "ticket_type == 'unknown' or confidence < 0.7"
            context: Dict of available variables

        Returns:
            True if condition matches (or condition is None/empty)

        Raises:
            ConditionEvaluationError: If condition syntax is invalid
        """
        # No condition = always true (unconditional transition)
        if not condition or condition.strip() == "":
            return True

        # Support compound conditions with 'or' and 'and'
        # 'or' has lower precedence: split on 'or' first, then 'and' within each clause
        if " or " in condition:
            sub_conditions = [c.strip() for c in condition.split(" or ")]
            return any(self._evaluate_and_clause(sub, context) for sub in sub_conditions)

        if " and " in condition:
            return self._evaluate_and_clause(condition, context)

        return self._evaluate_single(condition, context)

    def _evaluate_and_clause(self, condition: str, context: dict[str, Any]) -> bool:
        """Evaluate a condition that may contain 'and' conjunctions."""
        if " and " in condition:
            sub_conditions = [c.strip() for c in condition.split(" and ")]
            return all(self._evaluate_single(sub, context) for sub in sub_conditions)
        return self._evaluate_single(condition, context)

    def _evaluate_single(self, condition: str, context: dict[str, Any]) -> bool:
        """Evaluate a single (non-compound) condition."""
        match = self.CONDITION_PATTERN.match(condition)
        if not match:
            raise ConditionEvaluationError(
                f"Invalid condition syntax: '{condition}'. "
                f"Expected format: 'variable operator value'"
            )

        var_name, op_str, value_str = match.groups()

        # Get operator function
        op_func = self.OPERATORS.get(op_str)
        if not op_func:
            raise ConditionEvaluationError(f"Unknown operator: {op_str}")

        # Get variable value from context
        var_value = self._get_nested_value(context, var_name)
        if var_value is None:
            logger.warning(f"Variable '{var_name}' not found in context, defaulting to None")

        # Parse the comparison value
        compare_value = self._parse_value(value_str)

        # Evaluate
        try:
            result = op_func(var_value, compare_value)
            logger.debug(f"Condition '{condition}': {var_value} {op_str} {compare_value} = {result}")
            return bool(result)
        except TypeError as e:
            raise ConditionEvaluationError(
                f"Cannot compare {type(var_value).__name__} with {type(compare_value).__name__}: {e}"
            )

    def _get_nested_value(self, context: dict[str, Any], key: str) -> Any:
        """Get potentially nested value (e.g., 'classify_ticket.confidence')."""
        if "." in key:
            parts = key.split(".", 1)
            nested = context.get(parts[0])
            if isinstance(nested, dict):
                return nested.get(parts[1])
            return None
        return context.get(key)

    def _parse_value(self, value_str: str) -> Any:
        """Parse a value string into Python type."""
        value_str = value_str.strip()

        # Boolean
        if value_str.lower() == "true":
            return True
        if value_str.lower() == "false":
            return False

        # None/null
        if value_str.lower() in ("none", "null"):
            return None

        # String (quoted)
        if (value_str.startswith('"') and value_str.endswith('"')) or \
           (value_str.startswith("'") and value_str.endswith("'")):
            return value_str[1:-1]

        # Number
        try:
            if "." in value_str:
                return float(value_str)
            return int(value_str)
        except ValueError:
            pass

        # Unquoted string (legacy support)
        return value_str
