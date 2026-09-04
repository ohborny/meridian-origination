using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MortgageLOS
{
    // =========================================================================
    // FormatUtils - Formatting and utility functions
    //
    // A grab-bag of utilities that were "shared" but ended up being copy-pasted
    // into multiple modules anyway. Some of these are used, some aren't.
    // Nobody knows which is which. Good luck.
    // =========================================================================

    public static class FormatUtils
    {
        #region Money

        /// <summary>
        /// Formats a decimal as currency (USD).
        /// </summary>
        public static string FormatMoney(decimal amount)
        {
            return amount.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        }

        /// <summary>
        /// Formats money without the dollar sign (for reports).
        /// </summary>
        public static string FormatMoneyNoSymbol(decimal amount)
        {
            return amount.ToString("N2", CultureInfo.GetCultureInfo("en-US"));
        }

        /// <summary>
        /// Parses a money string, handling $ signs and commas.
        /// </summary>
        public static decimal ParseMoney(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0m;
            s = s.Replace("$", "").Replace(",", "").Trim();
            if (decimal.TryParse(s, out decimal result))
                return result;
            return 0m;
        }

        /// <summary>
        /// Rounds to the nearest cent (2 decimal places, banker's rounding).
        /// </summary>
        public static decimal RoundMoney(decimal amount)
        {
            return Math.Round(amount, 2, MidpointRounding.ToEven);
        }

        #endregion

        #region Percentage

        public static string FormatPercent(decimal value, int decimals = 3)
        {
            return value.ToString("F" + decimals) + "%";
        }

        public static string FormatRate(decimal rate)
        {
            return rate.ToString("F3") + "%";
        }

        #endregion

        #region SSN

        /// <summary>
        /// Masks SSN, showing only last 4 digits.
        /// </summary>
        public static string MaskSsn(string ssn)
        {
            if (string.IsNullOrEmpty(ssn) || ssn.Length < 4)
                return "***-**-****";
            string last4 = ssn.Substring(ssn.Length - 4);
            return "***-**-" + last4;
        }

        /// <summary>
        /// Gets the last 4 of an SSN.
        /// </summary>
        public static string GetSsnLast4(string ssn)
        {
            if (string.IsNullOrEmpty(ssn) || ssn.Length < 4)
                return "";
            return ssn.Substring(ssn.Length - 4);
        }

        /// <summary>
        /// Hashes an SSN using SHA256. Used for storage and matching across databases.
        /// </summary>
        public static string HashSsn(string ssn)
        {
            if (string.IsNullOrEmpty(ssn))
                return "";
            ssn = ssn.Replace("-", "").Replace(" ", "");
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(ssn));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Validates SSN format (basic check, not exhaustive).
        /// </summary>
        public static bool IsValidSsnFormat(string ssn)
        {
            if (string.IsNullOrEmpty(ssn))
                return false;
            // Remove dashes for validation
            string clean = ssn.Replace("-", "").Trim();
            if (clean.Length != 9)
                return false;
            // No area number of 000, 666, or 900-999
            int area = int.Parse(clean.Substring(0, 3));
            if (area == 0 || area == 666 || area >= 900)
                return false;
            // No group number of 00
            int group = int.Parse(clean.Substring(3, 2));
            if (group == 0)
                return false;
            // No serial number of 0000
            int serial = int.Parse(clean.Substring(5, 4));
            if (serial == 0)
                return false;
            return true;
        }

        #endregion

        #region Phone

        public static string FormatPhone(string phone)
        {
            if (string.IsNullOrEmpty(phone))
                return "";
            string digits = Regex.Replace(phone, @"[^\d]", "");
            if (digits.Length == 10)
                return string.Format("({0}) {1}-{2}", digits.Substring(0, 3), digits.Substring(3, 3), digits.Substring(6, 4));
            if (digits.Length == 11 && digits[0] == '1')
                return string.Format("+1 ({0}) {1}-{2}", digits.Substring(1, 3), digits.Substring(4, 3), digits.Substring(7, 4));
            return phone;
        }

        #endregion

        #region Date

        public static string FormatDate(DateTime date)
        {
            return date.ToString("MM/dd/yyyy");
        }

        public static string FormatDateShort(DateTime date)
        {
            return date.ToString("MM/dd/yy");
        }

        public static string FormatDateTime(DateTime dt)
        {
            return dt.ToString("MM/dd/yyyy HH:mm:ss");
        }

        public static string FormatDateForDb(DateTime date)
        {
            return date.ToString("yyyy-MM-dd");
        }

        public static string FormatTimestampForDb(DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static DateTime? SafeParseDate(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;
            string[] formats = { "MM/dd/yyyy", "yyyy-MM-dd", "MM/dd/yy", "M/d/yyyy", "M/d/yy" };
            foreach (string fmt in formats)
            {
                if (DateTime.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                    return result;
            }
            if (DateTime.TryParse(s, out DateTime parsed))
                return parsed;
            return null;
        }

        #endregion

        #region Loan Number

        /// <summary>
        /// Generates a loan number in the format: BRANCH-YYYY-NNNNN
        /// </summary>
        public static string GenerateLoanNumber(string branchCode, int sequence)
        {
            return string.Format("{0}-{1}-{2:D5}", branchCode, DateTime.Now.Year, sequence);
        }

        /// <summary>
        /// Validates loan number format.
        /// </summary>
        public static bool IsValidLoanNumber(string loanNumber)
        {
            if (string.IsNullOrEmpty(loanNumber))
                return false;
            return Regex.IsMatch(loanNumber, @"^[A-Z0-9]{2,10}-\d{4}-\d{5}$");
        }

        #endregion

        #region Misc

        /// <summary>
        /// Truncates a string to max length, adding ellipsis if truncated.
        /// </summary>
        public static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen)
                return s;
            if (maxLen <= 3)
                return s.Substring(0, maxLen);
            return s.Substring(0, maxLen - 3) + "...";
        }

        /// <summary>
        /// Converts a string to a safe SQL LIKE pattern.
        /// </summary>
        public static string ToLikePattern(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "%";
            return "%" + s.Replace("%", "\\%").Replace("_", "\\_") + "%";
        }

        /// <summary>
        /// Null-safe string comparison.
        /// </summary>
        public static bool SafeEquals(string a, string b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converts a decimal to a safe database value (handles null/NaN).
        /// </summary>
        public static object SafeDbValue(object value)
        {
            if (value == null)
                return DBNull.Value;
            return value;
        }

        // Added 2017 - not sure if this is used anywhere
        public static string GenerateGuid()
        {
            return Guid.NewGuid().ToString("N").ToUpperInvariant();
        }

        // Added 2019 for audit trail
        public static string GetMachineName()
        {
            try
            {
                return Environment.MachineName;
            }
            catch
            {
                return "unknown";
            }
        }

        #endregion
    }
}
