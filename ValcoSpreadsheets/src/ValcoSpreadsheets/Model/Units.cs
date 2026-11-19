namespace ValcoSpreadsheets.Grid
{
    using System;

    /// <summary>Converting between Excel units and screen pixels.</summary>
    public static class Units
    {
        public const double MaxDigitWidth = 7; // Calibri 11 at 96 dpi
        public static double ColWidthToPx(double w) => w <= 0 ? 0 : w < 1 ? Math.Round(w * (MaxDigitWidth + 5)) : Math.Round(w * MaxDigitWidth) + 5;
        public static double PxToColWidth(double px) => px <= 0 ? 0 : px < MaxDigitWidth + 5 ? Math.Round(px / (MaxDigitWidth + 5), 2) : Math.Round((px - 5) / MaxDigitWidth, 2);
        public static double PtToPx(double pt) => pt * 96.0 / 72.0;
        public static double PxToPt(double px) => px * 72.0 / 96.0;
    }

}
