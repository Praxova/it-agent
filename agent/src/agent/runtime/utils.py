"""Shared utilities for workflow executors."""
import json
import re


def resolve_template(template: str, context) -> str:
    """Replace {{variable}} placeholders with context variable values.

    Checks ``context.variables`` then ``context.ticket_data`` for each key.
    Dict values are serialised as JSON; ``None`` values leave the placeholder
    unchanged so the caller can detect unresolved variables.
    """
    if not template:
        return ""

    def replacer(match: re.Match) -> str:
        var_name = match.group(1).strip()
        # Try context variables first, then ticket_data
        value = context.get_variable(var_name)
        if value is None:
            value = context.ticket_data.get(var_name)
        if value is None:
            return match.group(0)  # Leave unreplaced
        if isinstance(value, dict):
            return json.dumps(value)
        return str(value)

    return re.sub(r"\{\{(\w+)\}\}", replacer, template)
