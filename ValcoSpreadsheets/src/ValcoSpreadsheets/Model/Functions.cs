using System;
using System.Collections.Generic;
using System.Linq;

namespace ValcoSpreadsheets.Model
{
    /// <summary>The worksheet functions offered by Insert Function and Formula AutoComplete (all evaluated by the calculation engine).</summary>
    public static class Functions
    {
        public record Fn(string Name, string Category, string Args, string Description);

        public static readonly string[] Categories = { "Most Recently Used", "All", "Financial", "Date & Time", "Math & Trig", "Statistical", "Lookup & Reference", "Database", "Text", "Logical", "Information" };

        public static readonly List<Fn> All = new List<Fn>
        {
            // Math & Trig
            new("ABS", "Math & Trig", "number", "Returns the absolute value of a number, a number without its sign."),
            new("ACOS", "Math & Trig", "number", "Returns the arccosine of a number, in radians."),
            new("ASIN", "Math & Trig", "number", "Returns the arcsine of a number, in radians."),
            new("ATAN", "Math & Trig", "number", "Returns the arctangent of a number, in radians."),
            new("ATAN2", "Math & Trig", "x_num, y_num", "Returns the arctangent of the x- and y-coordinates, in radians."),
            new("CEILING", "Math & Trig", "number, significance", "Rounds a number up to the nearest multiple of significance."),
            new("COMBIN", "Math & Trig", "number, number_chosen", "Returns the number of combinations for a given number of items."),
            new("COS", "Math & Trig", "number", "Returns the cosine of an angle."),
            new("DEGREES", "Math & Trig", "angle", "Converts radians to degrees."),
            new("EVEN", "Math & Trig", "number", "Rounds a number up to the nearest even integer."),
            new("EXP", "Math & Trig", "number", "Returns e raised to the power of a number."),
            new("FACT", "Math & Trig", "number", "Returns the factorial of a number."),
            new("FLOOR", "Math & Trig", "number, significance", "Rounds a number down to the nearest multiple of significance."),
            new("GCD", "Math & Trig", "number1, [number2], ...", "Returns the greatest common divisor."),
            new("INT", "Math & Trig", "number", "Rounds a number down to the nearest integer."),
            new("LCM", "Math & Trig", "number1, [number2], ...", "Returns the least common multiple."),
            new("LN", "Math & Trig", "number", "Returns the natural logarithm of a number."),
            new("LOG", "Math & Trig", "number, [base]", "Returns the logarithm of a number to the base you specify."),
            new("LOG10", "Math & Trig", "number", "Returns the base-10 logarithm of a number."),
            new("MOD", "Math & Trig", "number, divisor", "Returns the remainder after a number is divided by a divisor."),
            new("MROUND", "Math & Trig", "number, multiple", "Returns a number rounded to the desired multiple."),
            new("ODD", "Math & Trig", "number", "Rounds a number up to the nearest odd integer."),
            new("PI", "Math & Trig", "", "Returns the value of pi, 3.14159265358979."),
            new("POWER", "Math & Trig", "number, power", "Returns the result of a number raised to a power."),
            new("PRODUCT", "Math & Trig", "number1, [number2], ...", "Multiplies all the numbers given as arguments."),
            new("QUOTIENT", "Math & Trig", "numerator, denominator", "Returns the integer portion of a division."),
            new("RADIANS", "Math & Trig", "angle", "Converts degrees to radians."),
            new("RAND", "Math & Trig", "", "Returns a random number greater than or equal to 0 and less than 1."),
            new("RANDBETWEEN", "Math & Trig", "bottom, top", "Returns a random number between the numbers you specify."),
            new("ROMAN", "Math & Trig", "number, [form]", "Converts an Arabic numeral to Roman, as text."),
            new("ROUND", "Math & Trig", "number, num_digits", "Rounds a number to a specified number of digits."),
            new("ROUNDDOWN", "Math & Trig", "number, num_digits", "Rounds a number down, toward zero."),
            new("ROUNDUP", "Math & Trig", "number, num_digits", "Rounds a number up, away from zero."),
            new("SIGN", "Math & Trig", "number", "Returns the sign of a number: 1 if positive, 0 if zero, -1 if negative."),
            new("SIN", "Math & Trig", "number", "Returns the sine of an angle."),
            new("SQRT", "Math & Trig", "number", "Returns a positive square root."),
            new("SUBTOTAL", "Math & Trig", "function_num, ref1, ...", "Returns a subtotal in a list or database."),
            new("SUM", "Math & Trig", "number1, [number2], ...", "Adds all the numbers in a range of cells."),
            new("SUMIF", "Math & Trig", "range, criteria, [sum_range]", "Adds the cells specified by a given condition or criteria."),
            new("SUMIFS", "Math & Trig", "sum_range, criteria_range1, criteria1, ...", "Adds the cells specified by a given set of conditions or criteria."),
            new("SUMPRODUCT", "Math & Trig", "array1, [array2], ...", "Returns the sum of the products of corresponding ranges or arrays."),
            new("SUMSQ", "Math & Trig", "number1, [number2], ...", "Returns the sum of the squares of the arguments."),
            new("TAN", "Math & Trig", "number", "Returns the tangent of an angle."),
            new("TRUNC", "Math & Trig", "number, [num_digits]", "Truncates a number to an integer by removing the fractional part."),
            // Statistical
            new("AVERAGE", "Statistical", "number1, [number2], ...", "Returns the average (arithmetic mean) of its arguments."),
            new("AVERAGEA", "Statistical", "value1, [value2], ...", "Returns the average of its arguments, counting text and FALSE as 0 and TRUE as 1."),
            new("AVERAGEIF", "Statistical", "range, criteria, [average_range]", "Finds the average for the cells specified by a given condition or criteria."),
            new("AVERAGEIFS", "Statistical", "average_range, criteria_range1, criteria1, ...", "Finds the average for the cells specified by a given set of conditions."),
            new("COUNT", "Statistical", "value1, [value2], ...", "Counts the number of cells in a range that contain numbers."),
            new("COUNTA", "Statistical", "value1, [value2], ...", "Counts the number of cells in a range that are not empty."),
            new("COUNTBLANK", "Statistical", "range", "Counts the number of empty cells in a range."),
            new("COUNTIF", "Statistical", "range, criteria", "Counts the number of cells within a range that meet the given condition."),
            new("COUNTIFS", "Statistical", "criteria_range1, criteria1, ...", "Counts the number of cells specified by a given set of conditions."),
            new("LARGE", "Statistical", "array, k", "Returns the k-th largest value in a data set."),
            new("MAX", "Statistical", "number1, [number2], ...", "Returns the largest value in a set of values."),
            new("MEDIAN", "Statistical", "number1, [number2], ...", "Returns the median, the number in the middle of the given numbers."),
            new("MIN", "Statistical", "number1, [number2], ...", "Returns the smallest number in a set of values."),
            new("MODE", "Statistical", "number1, [number2], ...", "Returns the most frequently occurring value in a range."),
            new("PERCENTILE", "Statistical", "array, k", "Returns the k-th percentile of values in a range."),
            new("RANK", "Statistical", "number, ref, [order]", "Returns the rank of a number in a list of numbers."),
            new("SMALL", "Statistical", "array, k", "Returns the k-th smallest value in a data set."),
            new("STDEV", "Statistical", "number1, [number2], ...", "Estimates standard deviation based on a sample."),
            new("STDEVP", "Statistical", "number1, [number2], ...", "Calculates standard deviation based on the entire population."),
            new("VAR", "Statistical", "number1, [number2], ...", "Estimates variance based on a sample."),
            new("VARP", "Statistical", "number1, [number2], ...", "Calculates variance based on the entire population."),
            // Logical
            new("AND", "Logical", "logical1, [logical2], ...", "Checks whether all arguments are TRUE, and returns TRUE if they are."),
            new("FALSE", "Logical", "", "Returns the logical value FALSE."),
            new("IF", "Logical", "logical_test, [value_if_true], [value_if_false]", "Checks whether a condition is met, and returns one value if TRUE and another if FALSE."),
            new("IFERROR", "Logical", "value, value_if_error", "Returns value_if_error if the expression is an error, and the value of the expression itself otherwise."),
            new("NOT", "Logical", "logical", "Changes FALSE to TRUE, or TRUE to FALSE."),
            new("OR", "Logical", "logical1, [logical2], ...", "Checks whether any of the arguments are TRUE."),
            new("TRUE", "Logical", "", "Returns the logical value TRUE."),
            // Text
            new("CHAR", "Text", "number", "Returns the character specified by the code number."),
            new("CLEAN", "Text", "text", "Removes all nonprintable characters from text."),
            new("CODE", "Text", "text", "Returns a numeric code for the first character in a text string."),
            new("CONCATENATE", "Text", "text1, [text2], ...", "Joins several text strings into one text string."),
            new("DOLLAR", "Text", "number, [decimals]", "Converts a number to text, using currency format."),
            new("EXACT", "Text", "text1, text2", "Checks whether two text strings are exactly the same."),
            new("FIND", "Text", "find_text, within_text, [start_num]", "Returns the starting position of one text string within another (case-sensitive)."),
            new("FIXED", "Text", "number, [decimals], [no_commas]", "Rounds a number and returns the result as text."),
            new("LEFT", "Text", "text, [num_chars]", "Returns the specified number of characters from the start of a text string."),
            new("LEN", "Text", "text", "Returns the number of characters in a text string."),
            new("LOWER", "Text", "text", "Converts all letters in a text string to lowercase."),
            new("MID", "Text", "text, start_num, num_chars", "Returns the characters from the middle of a text string."),
            new("PROPER", "Text", "text", "Converts a text string to proper case: the first letter of each word in uppercase."),
            new("REPLACE", "Text", "old_text, start_num, num_chars, new_text", "Replaces part of a text string with a different text string."),
            new("REPT", "Text", "text, number_times", "Repeats text a given number of times."),
            new("RIGHT", "Text", "text, [num_chars]", "Returns the specified number of characters from the end of a text string."),
            new("SEARCH", "Text", "find_text, within_text, [start_num]", "Returns the position of one text string within another (not case-sensitive)."),
            new("SUBSTITUTE", "Text", "text, old_text, new_text, [instance_num]", "Replaces existing text with new text in a text string."),
            new("T", "Text", "value", "Checks whether a value is text, and returns the text if it is."),
            new("TEXT", "Text", "value, format_text", "Converts a value to text in a specific number format."),
            new("TRIM", "Text", "text", "Removes all spaces from a text string except for single spaces between words."),
            new("UPPER", "Text", "text", "Converts a text string to all uppercase letters."),
            new("VALUE", "Text", "text", "Converts a text string that represents a number to a number."),
            // Date & Time
            new("DATE", "Date & Time", "year, month, day", "Returns the number that represents the date."),
            new("DATEDIF", "Date & Time", "start_date, end_date, unit", "Calculates the number of days, months or years between two dates."),
            new("DATEVALUE", "Date & Time", "date_text", "Converts a date in the form of text to a number that represents the date."),
            new("DAY", "Date & Time", "serial_number", "Returns the day of the month, a number from 1 to 31."),
            new("DAYS360", "Date & Time", "start_date, end_date, [method]", "Returns the number of days between two dates based on a 360-day year."),
            new("EDATE", "Date & Time", "start_date, months", "Returns the date that is the indicated number of months before or after the start date."),
            new("EOMONTH", "Date & Time", "start_date, months", "Returns the last day of the month before or after a specified number of months."),
            new("HOUR", "Date & Time", "serial_number", "Returns the hour as a number from 0 (12:00 A.M.) to 23 (11:00 P.M.)."),
            new("MINUTE", "Date & Time", "serial_number", "Returns the minute, a number from 0 to 59."),
            new("MONTH", "Date & Time", "serial_number", "Returns the month, a number from 1 (January) to 12 (December)."),
            new("NETWORKDAYS", "Date & Time", "start_date, end_date, [holidays]", "Returns the number of whole workdays between two dates."),
            new("NOW", "Date & Time", "", "Returns the current date and time formatted as a date and time."),
            new("SECOND", "Date & Time", "serial_number", "Returns the second, a number from 0 to 59."),
            new("TIME", "Date & Time", "hour, minute, second", "Converts hours, minutes and seconds given as numbers to a time."),
            new("TIMEVALUE", "Date & Time", "time_text", "Converts a text time to a number that represents the time."),
            new("TODAY", "Date & Time", "", "Returns the current date formatted as a date."),
            new("WEEKDAY", "Date & Time", "serial_number, [return_type]", "Returns a number from 1 to 7 identifying the day of the week of a date."),
            new("WEEKNUM", "Date & Time", "serial_number, [return_type]", "Returns the week number in the year."),
            new("WORKDAY", "Date & Time", "start_date, days, [holidays]", "Returns the date before or after a specified number of workdays."),
            new("YEAR", "Date & Time", "serial_number", "Returns the year of a date, an integer in the range 1900-9999."),
            new("YEARFRAC", "Date & Time", "start_date, end_date, [basis]", "Returns the year fraction representing the number of whole days between two dates."),
            // Lookup & Reference
            new("ADDRESS", "Lookup & Reference", "row_num, column_num, [abs_num], [a1], [sheet_text]", "Creates a cell reference as text, given row and column numbers."),
            new("CHOOSE", "Lookup & Reference", "index_num, value1, [value2], ...", "Chooses a value or action to perform from a list of values, based on an index number."),
            new("COLUMN", "Lookup & Reference", "[reference]", "Returns the column number of a reference."),
            new("COLUMNS", "Lookup & Reference", "array", "Returns the number of columns in an array or reference."),
            new("HLOOKUP", "Lookup & Reference", "lookup_value, table_array, row_index_num, [range_lookup]", "Looks for a value in the top row of a table, and returns the value in the same column from a row you specify."),
            new("HYPERLINK", "Lookup & Reference", "link_location, [friendly_name]", "Creates a shortcut that opens a document or web page."),
            new("INDEX", "Lookup & Reference", "array, row_num, [column_num]", "Returns a value or reference of the cell at the intersection of a particular row and column."),
            new("INDIRECT", "Lookup & Reference", "ref_text, [a1]", "Returns the reference specified by a text string."),
            new("LOOKUP", "Lookup & Reference", "lookup_value, lookup_vector, [result_vector]", "Looks up a value either from a one-row or one-column range or from an array."),
            new("MATCH", "Lookup & Reference", "lookup_value, lookup_array, [match_type]", "Returns the relative position of an item in an array that matches a specified value."),
            new("OFFSET", "Lookup & Reference", "reference, rows, cols, [height], [width]", "Returns a reference to a range that is a given number of rows and columns from a given reference."),
            new("ROW", "Lookup & Reference", "[reference]", "Returns the row number of a reference."),
            new("ROWS", "Lookup & Reference", "array", "Returns the number of rows in a reference or array."),
            new("TRANSPOSE", "Lookup & Reference", "array", "Converts a vertical range of cells to a horizontal range, or vice versa."),
            new("VLOOKUP", "Lookup & Reference", "lookup_value, table_array, col_index_num, [range_lookup]", "Looks for a value in the leftmost column of a table, and returns a value in the same row from a column you specify."),
            // Financial
            new("FV", "Financial", "rate, nper, pmt, [pv], [type]", "Returns the future value of an investment based on periodic, constant payments and a constant interest rate."),
            new("IPMT", "Financial", "rate, per, nper, pv, [fv], [type]", "Returns the interest payment for a given period of an investment."),
            new("IRR", "Financial", "values, [guess]", "Returns the internal rate of return for a series of cash flows."),
            new("NPER", "Financial", "rate, pmt, pv, [fv], [type]", "Returns the number of periods for an investment."),
            new("NPV", "Financial", "rate, value1, [value2], ...", "Returns the net present value of an investment based on a discount rate and future payments."),
            new("PMT", "Financial", "rate, nper, pv, [fv], [type]", "Calculates the payment for a loan based on constant payments and a constant interest rate."),
            new("PPMT", "Financial", "rate, per, nper, pv, [fv], [type]", "Returns the payment on the principal for a given investment."),
            new("PV", "Financial", "rate, nper, pmt, [fv], [type]", "Returns the present value of an investment."),
            new("RATE", "Financial", "nper, pmt, pv, [fv], [type], [guess]", "Returns the interest rate per period of a loan or an investment."),
            new("SLN", "Financial", "cost, salvage, life", "Returns the straight-line depreciation of an asset for one period."),
            // Information
            new("ISBLANK", "Information", "value", "Checks whether a reference is to an empty cell, and returns TRUE or FALSE."),
            new("ISERR", "Information", "value", "Checks whether a value is an error other than #N/A."),
            new("ISERROR", "Information", "value", "Checks whether a value is an error."),
            new("ISEVEN", "Information", "number", "Returns TRUE if the number is even."),
            new("ISLOGICAL", "Information", "value", "Checks whether a value is a logical value (TRUE or FALSE)."),
            new("ISNA", "Information", "value", "Checks whether a value is #N/A."),
            new("ISNONTEXT", "Information", "value", "Checks whether a value is not text."),
            new("ISNUMBER", "Information", "value", "Checks whether a value is a number."),
            new("ISODD", "Information", "number", "Returns TRUE if the number is odd."),
            new("ISTEXT", "Information", "value", "Checks whether a value is text."),
            new("N", "Information", "value", "Converts a non-number value to a number, dates to serial numbers, TRUE to 1."),
            new("NA", "Information", "", "Returns the error value #N/A."),
            new("TYPE", "Information", "value", "Returns an integer representing the data type of a value."),
            // Database
            new("DAVERAGE", "Database", "database, field, criteria", "Averages the values in a column of a list or database that match conditions you specify."),
            new("DCOUNT", "Database", "database, field, criteria", "Counts the cells containing numbers in a database that match conditions you specify."),
            new("DMAX", "Database", "database, field, criteria", "Returns the largest number in a database column that matches conditions you specify."),
            new("DMIN", "Database", "database, field, criteria", "Returns the smallest number in a database column that matches conditions you specify."),
            new("DSUM", "Database", "database, field, criteria", "Adds the numbers in a database column that match conditions you specify."),
        };

        public static readonly Dictionary<string, Fn> ByName = All.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<Fn> StartingWith(string prefix) =>
            All.Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Name);

        public static List<string> Recent { get; } = new List<string> { "SUM", "AVERAGE", "IF", "HYPERLINK", "COUNT", "MAX", "SIN", "SUMIF", "PMT", "STDEV" };

        public static void Used(string name)
        {
            Recent.Remove(name.ToUpperInvariant());
            Recent.Insert(0, name.ToUpperInvariant());
            if (Recent.Count > 10) Recent.RemoveAt(10);
        }

        /// <summary>The function whose argument list the caret is in, and the argument index (for the ScreenTip).</summary>
        public static (string Name, int Arg)? CallAt(string formula, int caret)
        {
            if (string.IsNullOrEmpty(formula) || formula[0] != '=') return null;
            caret = Math.Min(caret, formula.Length);
            var stack = new Stack<(string Name, int Arg)>();
            bool inStr = false;
            for (int i = 1; i < caret; i++)
            {
                char ch = formula[i];
                if (ch == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (ch == '(')
                {
                    int j = i - 1;
                    while (j >= 1 && (char.IsLetterOrDigit(formula[j]) || formula[j] == '.' || formula[j] == '_')) j--;
                    stack.Push((formula.Substring(j + 1, i - j - 1), 0));
                }
                else if (ch == ')') { if (stack.Count > 0) stack.Pop(); }
                else if (ch == ',' && stack.Count > 0) { var t = stack.Pop(); stack.Push((t.Name, t.Arg + 1)); }
            }
            return stack.Count > 0 && stack.Peek().Name.Length > 0 ? stack.Peek() : null;
        }

        /// <summary>The partly typed function name before the caret, if the caret is at the end of a name.</summary>
        public static string TokenAt(string formula, int caret)
        {
            if (string.IsNullOrEmpty(formula) || formula[0] != '=' || caret < 2) return null;
            int quotes = formula.Take(caret).Count(c => c == '"');
            if (quotes % 2 == 1) return null;
            int j = caret - 1;
            while (j >= 1 && (char.IsLetterOrDigit(formula[j]) || formula[j] == '.')) j--;
            var tok = formula.Substring(j + 1, caret - j - 1);
            if (tok.Length == 0 || !char.IsLetter(tok[0])) return null;
            if (j >= 1 && (formula[j] == '!' || formula[j] == '$' || formula[j] == ':')) return null;
            return tok;
        }

        /// <summary>The cell references in a formula, for the coloured outlines while editing.</summary>
        public static List<CellRect> References(string formula)
        {
            var list = new List<CellRect>();
            if (string.IsNullOrEmpty(formula) || formula[0] != '=') return list;
            var rx = new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z0-9_!.""])(\$?[A-Za-z]{1,3}\$?\d{1,7})(:\$?[A-Za-z]{1,3}\$?\d{1,7})?(?![A-Za-z0-9_(])");
            bool inStr = false; var clean = new System.Text.StringBuilder();
            foreach (var ch in formula) { if (ch == '"') inStr = !inStr; clean.Append(inStr ? ' ' : ch); }
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(clean.ToString()))
                if (A1.TryParse(m.Value, out var r)) list.Add(r);
            return list;
        }
    }
}
