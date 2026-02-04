"""Tests for condition evaluator."""
import pytest
from agent.runtime.condition_evaluator import ConditionEvaluator, ConditionEvaluationError


class TestConditionEvaluator:
    def setup_method(self):
        self.evaluator = ConditionEvaluator()

    def test_empty_condition_returns_true(self):
        assert self.evaluator.evaluate(None, {}) is True
        assert self.evaluator.evaluate("", {}) is True

    def test_numeric_comparison(self):
        ctx = {"confidence": 0.85}
        assert self.evaluator.evaluate("confidence >= 0.8", ctx) is True
        assert self.evaluator.evaluate("confidence < 0.8", ctx) is False
        assert self.evaluator.evaluate("confidence >= 0.9", ctx) is False

    def test_boolean_comparison(self):
        ctx = {"valid": True, "success": False}
        assert self.evaluator.evaluate("valid == true", ctx) is True
        assert self.evaluator.evaluate("valid == false", ctx) is False
        assert self.evaluator.evaluate("success == false", ctx) is True

    def test_string_comparison(self):
        ctx = {"ticket_type": "password_reset"}
        assert self.evaluator.evaluate('ticket_type == "password_reset"', ctx) is True
        assert self.evaluator.evaluate("ticket_type == 'password_reset'", ctx) is True

    def test_nested_value(self):
        ctx = {"classify_ticket": {"confidence": 0.9}}
        assert self.evaluator.evaluate("classify_ticket.confidence >= 0.8", ctx) is True

    def test_invalid_syntax(self):
        with pytest.raises(ConditionEvaluationError):
            self.evaluator.evaluate("invalid syntax here", {})

    def test_missing_variable(self):
        # Missing variable causes TypeError when comparing None with float
        # This is caught and re-raised as ConditionEvaluationError
        with pytest.raises(ConditionEvaluationError):
            self.evaluator.evaluate("missing >= 0.5", {})
