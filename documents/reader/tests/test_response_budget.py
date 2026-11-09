import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from response_budget import ResponseBudget, utf8_size


class ResponseBudgetTests(unittest.TestCase):
    def test_utf8_size_counts_bytes(self):
        self.assertEqual(0, utf8_size(None))
        self.assertEqual(0, utf8_size(""))
        self.assertEqual(3, utf8_size("abc"))
        self.assertEqual(6, utf8_size("測試"))
        self.assertEqual(4, utf8_size("\U0001F600"))

    def test_constructor_rejects_non_positive_limit(self):
        for value in (0, -1):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    ResponseBudget(value)

    def test_take_text_returns_whole_text_when_it_fits(self):
        budget = ResponseBudget(10)

        self.assertEqual("hello", budget.take_text("hello"))

        self.assertEqual(5, budget.used)
        self.assertEqual(5, budget.remaining)
        self.assertFalse(budget.exhausted)
        self.assertFalse(budget.closed)

    def test_take_text_accepts_text_that_exactly_fills_budget(self):
        budget = ResponseBudget(5)

        self.assertEqual("hello", budget.take_text("hello"))

        self.assertEqual(0, budget.remaining)
        self.assertFalse(budget.exhausted)
        self.assertTrue(budget.closed)

    def test_take_text_cuts_ascii_at_limit(self):
        budget = ResponseBudget(3)

        self.assertEqual("hel", budget.take_text("hello"))

        self.assertTrue(budget.exhausted)
        self.assertTrue(budget.closed)
        self.assertEqual(3, budget.used)
        self.assertEqual(5, budget.observed)

    def test_take_text_cuts_at_character_boundary(self):
        for limit, expected in ((1, ""), (2, ""), (3, "測"), (4, "測"), (5, "測"), (6, "測試")):
            with self.subTest(limit=limit):
                budget = ResponseBudget(limit)

                actual = budget.take_text("測試文字")

                self.assertEqual(expected, actual)
                self.assertEqual(utf8_size(expected), budget.used)
                self.assertTrue(budget.exhausted)

    def test_take_text_cuts_surrogate_pair_character_whole(self):
        budget = ResponseBudget(5)

        self.assertEqual("a\U0001F600", budget.take_text("a\U0001F600b"))
        self.assertEqual(5, budget.used)

    def test_take_text_after_budget_is_full_returns_empty_and_marks_exhausted(self):
        budget = ResponseBudget(3)
        budget.take_text("abc")

        self.assertEqual("", budget.take_text("def"))

        self.assertTrue(budget.exhausted)
        self.assertEqual(6, budget.observed)

    def test_take_text_empty_text_does_not_exhaust(self):
        budget = ResponseBudget(3)
        budget.take_text("abc")

        self.assertEqual("", budget.take_text(""))

        self.assertFalse(budget.exhausted)

    def test_try_add_is_all_or_nothing(self):
        budget = ResponseBudget(10)

        self.assertTrue(budget.try_add(6))
        self.assertFalse(budget.try_add(5))
        self.assertTrue(budget.try_add(4))

        self.assertEqual(10, budget.used)
        self.assertEqual(15, budget.observed)
        self.assertTrue(budget.exhausted)

    def test_try_add_rejection_does_not_close_budget(self):
        budget = ResponseBudget(10)

        budget.try_add(11)

        self.assertTrue(budget.exhausted)
        self.assertFalse(budget.closed)
        self.assertEqual(10, budget.remaining)

    def test_try_add_rejects_negative_size(self):
        with self.assertRaises(ValueError):
            ResponseBudget(10).try_add(-1)

    def test_mark_incomplete_exhausts_and_closes(self):
        budget = ResponseBudget(10)

        budget.mark_incomplete()

        self.assertTrue(budget.exhausted)
        self.assertTrue(budget.closed)
        self.assertEqual(0, budget.used)

    def test_fields_uses_observed_bytes_as_inexact_by_default(self):
        budget = ResponseBudget(4)
        budget.take_text("abcdefgh")

        self.assertEqual(
            {"truncated": True, "original_bytes": 8, "original_bytes_exact": False, "max_response_bytes": 4},
            budget.fields(),
        )

    def test_fields_uses_known_original_size_with_exact_flag(self):
        budget = ResponseBudget(4)
        budget.take_text("abcdefgh")

        budget.mark_incomplete(original_bytes=5000, original_bytes_exact=True)

        self.assertEqual(
            {"truncated": True, "original_bytes": 5000, "original_bytes_exact": True, "max_response_bytes": 4},
            budget.fields(),
        )

    def test_fields_reports_known_original_size_as_inexact_when_requested(self):
        budget = ResponseBudget(4)

        budget.mark_incomplete(original_bytes=7)

        self.assertFalse(budget.fields()["original_bytes_exact"])


if __name__ == "__main__":
    unittest.main()
