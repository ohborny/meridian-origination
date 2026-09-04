using System;
using System.Collections.Generic;
using System.Linq;

namespace MortgageLOS
{
    // =========================================================================
    // DateUtils - Business day calculations
    //
    // CRITICAL: These methods are used for TRID compliance timing. Getting these
    // wrong means regulatory violations. The holiday list is HARDCODED and must
    // be updated every year. If you forget to update it, loans will have wrong
    // disclosure delivery dates. This happened in 2021 (see INC-2021-032).
    //
    // TODO: Move holidays to a database table (requested 2018, still not done)
    // TODO: Add support for state-specific holidays (some states have different
    //       holidays, but we currently use federal holidays for everything)
    // =========================================================================

    public static class DateUtils
    {
        /// <summary>
        /// Returns true if the date is a weekend (Saturday or Sunday).
        /// </summary>
        public static bool IsWeekend(DateTime date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }

        /// <summary>
        /// Returns true if the date is a US federal holiday.
        /// </summary>
        public static bool IsHoliday(DateTime date)
        {
            string dateStr = date.ToString("yyyy-MM-dd");
            return GetAllHolidays().Contains(dateStr);
        }

        /// <summary>
        /// Returns true if the date is a business day (not weekend, not holiday).
        /// </summary>
        public static bool IsBusinessDay(DateTime date)
        {
            return !IsWeekend(date) && !IsHoliday(date);
        }

        /// <summary>
        /// Adds N business days to a date. Used for TRID timing.
        /// </summary>
        public static DateTime AddBusinessDays(DateTime startDate, int businessDays)
        {
            DateTime result = startDate;
            int added = 0;
            while (added < businessDays)
            {
                result = result.AddDays(1);
                if (IsBusinessDay(result))
                {
                    added++;
                }
            }
            return result;
        }

        /// <summary>
        /// Subtracts N business days from a date.
        /// </summary>
        public static DateTime SubtractBusinessDays(DateTime startDate, int businessDays)
        {
            DateTime result = startDate;
            int subtracted = 0;
            while (subtracted < businessDays)
            {
                result = result.AddDays(-1);
                if (IsBusinessDay(result))
                {
                    subtracted++;
                }
            }
            return result;
        }

        /// <summary>
        /// Counts business days between two dates (exclusive of end date).
        /// </summary>
        public static int CountBusinessDays(DateTime startDate, DateTime endDate)
        {
            if (startDate > endDate)
            {
                return -CountBusinessDays(endDate, startDate);
            }

            int count = 0;
            DateTime current = startDate;
            while (current < endDate)
            {
                if (IsBusinessDay(current))
                {
                    count++;
                }
                current = current.AddDays(1);
            }
            return count;
        }

        /// <summary>
        /// Returns the next business day after the given date.
        /// </summary>
        public static DateTime NextBusinessDay(DateTime date)
        {
            return AddBusinessDays(date, 1);
        }

        /// <summary>
        /// Returns the previous business day before the given date.
        /// </summary>
        public static DateTime PreviousBusinessDay(DateTime date)
        {
            return SubtractBusinessDays(date, 1);
        }

        /// <summary>
        /// Calculates the Loan Estimate delivery deadline.
        /// Per TRID rule: LE must be delivered within 3 business days of application.
        /// </summary>
        public static DateTime CalculateLeDeadline(DateTime applicationDate)
        {
            return AddBusinessDays(applicationDate, 3);
        }

        /// <summary>
        /// Calculates the Closing Disclosure waiting period.
        /// Per TRID rule: CD must be received by borrower at least 3 business days
        /// before consummation (closing).
        /// </summary>
        public static DateTime CalculateCdConsummationDate(DateTime cdReceivedDate)
        {
            return AddBusinessDays(cdReceivedDate, 3);
        }

        /// <summary>
        /// Calculates when a revised LE is due after a changed circumstance.
        /// Per TRID: within 3 business days of the changed circumstance.
        /// </summary>
        public static DateTime CalculateRevisedLeDeadline(DateTime changedCircumstanceDate)
        {
            return AddBusinessDays(changedCircumstanceDate, 3);
        }

        /// <summary>
        /// Gets all known holidays as a set of date strings.
        /// </summary>
        public static HashSet<string> GetAllHolidays()
        {
            HashSet<string> holidays = new HashSet<string>();
            // Add hardcoded holidays from the Enums file
            foreach (string h in Holidays.Holidays2024) holidays.Add(h);
            foreach (string h in Holidays.Holidays2025) holidays.Add(h);
            foreach (string h in Holidays.Holidays2026) holidays.Add(h);
            return holidays;
        }

        /// <summary>
        /// Calculates age at a given date (for HMDA reporting).
        /// </summary>
        public static int CalculateAge(DateTime dateOfBirth, DateTime asOfDate)
        {
            int age = asOfDate.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > asOfDate.AddYears(-age))
            {
                age--;
            }
            return age;
        }

        // Added 2019 for rate lock expiry calculations
        /// <summary>
        /// Calculates rate lock expiry date (calendar days, not business days).
        /// Rate locks use calendar days, not business days. Don't confuse the two.
        /// </summary>
        public static DateTime CalculateRateLockExpiry(DateTime lockDate, int lockDays)
        {
            return lockDate.AddDays(lockDays);
        }

        // Added 2020 for TRID tolerance cure calculations
        /// <summary>
        /// Checks if a fee variance exceeds the 10% tolerance threshold.
        /// </summary>
        public static bool ExceedsTolerance(decimal leAmount, decimal cdAmount, decimal tolerancePct = 10.0m)
        {
            if (leAmount == 0) return cdAmount > 0;
            decimal variance = Math.Abs(cdAmount - leAmount);
            decimal pct = (variance / leAmount) * 100m;
            return pct > tolerancePct;
        }
    }
}
