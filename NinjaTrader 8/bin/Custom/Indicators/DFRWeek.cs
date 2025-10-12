#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class DFRWeek : Indicator
    {
        private bool inDFRPeriod = false;
        private int currentDFRStartBar = -1;
        private double currentDFRHigh = double.MinValue;
        private double currentDFRLow = double.MaxValue;
        private double activeDFRHigh = double.MinValue;
        private double activeDFRLow = double.MaxValue;
        private int activeDFRStartBar = -1;
        private DateTime currentWeekStart = DateTime.MinValue;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Draws a shaded box from wick to wick between Monday 1:00 PM and 4:59 PM EST with Fib projections for the week";
                Name = "DFRWeek";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                // All defaults are BLACK with 2x thickness
                BoxColor = Brushes.Black;
                BoxOpacity = 30;
                BorderColor = Brushes.Black;
                BorderThickness = 2;
                BorderStyle = DashStyleHelper.Dot;
                
                // Fib level settings - Above levels
                ShowLevel_Pos15 = true;
                Level_Pos15_Color = Brushes.Black;
                Level_Pos15_Thickness = 2;
                Level_Pos15_Style = DashStyleHelper.Dash;
                
                Level_Pos20_Color = Brushes.Black;
                Level_Pos20_Thickness = 2;
                Level_Pos20_Style = DashStyleHelper.Dash;
                
                Level_Pos25_Color = Brushes.Black;
                Level_Pos25_Thickness = 2;
                Level_Pos25_Style = DashStyleHelper.Dash;
                
                // Fib level settings - Below levels
                ShowLevel_Neg05 = true;
                Level_Neg05_Color = Brushes.Black;
                Level_Neg05_Thickness = 2;
                Level_Neg05_Style = DashStyleHelper.Dash;
                
                Level_Neg10_Color = Brushes.Black;
                Level_Neg10_Thickness = 2;
                Level_Neg10_Style = DashStyleHelper.Dash;
                
                Level_Neg15_Color = Brushes.Black;
                Level_Neg15_Thickness = 2;
                Level_Neg15_Style = DashStyleHelper.Dash;
            }
            else if (State == State.Configure)
            {
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1)
                return;
            
            // Use the chart's time directly (already in EST)
            int hour = Time[0].Hour;
            int minute = Time[0].Minute;
            DayOfWeek dayOfWeek = Time[0].DayOfWeek;
            
            // Check if we're starting a new week (Monday at or after midnight)
            if (dayOfWeek == DayOfWeek.Monday && hour == 0 && minute == 0)
            {
                DateTime weekStart = Time[0].Date;
                if (weekStart != currentWeekStart)
                {
                    currentWeekStart = weekStart;
                    // Store the previous DFR as the active one for projections
                    if (currentDFRHigh != double.MinValue && currentDFRLow != double.MaxValue)
                    {
                        activeDFRHigh = currentDFRHigh;
                        activeDFRLow = currentDFRLow;
                        activeDFRStartBar = CurrentBar;
                    }
                }
            }
            
            // Check if we're in the DFR period (Monday 1:00 PM to 4:59 PM EST)
            bool isDFRTime = (dayOfWeek == DayOfWeek.Monday && hour >= 1 && hour <= 16);
            
            if (isDFRTime)
            {
                if (!inDFRPeriod)
                {
                    // Starting a new DFR period
                    inDFRPeriod = true;
                    currentDFRStartBar = CurrentBar;
                    currentDFRHigh = High[0];
                    currentDFRLow = Low[0];
                }
                else
                {
                    // Update the high and low during the DFR period
                    currentDFRHigh = Math.Max(currentDFRHigh, High[0]);
                    currentDFRLow = Math.Min(currentDFRLow, Low[0]);
                }
            }
            else
            {
                if (inDFRPeriod)
                {
                    // Ending the DFR period, draw the box
                    inDFRPeriod = false;
                    
                    int barsAgo = CurrentBar - currentDFRStartBar;
                    string boxTag = "DFR_Box_" + currentDFRStartBar;
                    string borderTag = "DFR_Border_" + currentDFRStartBar;
                    
                    // Create semi-transparent brush
                    Brush fillBrush = BoxColor.Clone();
                    fillBrush.Opacity = BoxOpacity / 100.0;
                    fillBrush.Freeze();
                    
                    // Draw the filled rectangle
                    Draw.Rectangle(this, boxTag, false, 
                        barsAgo, currentDFRHigh, 0, currentDFRLow, 
                        fillBrush, fillBrush, 0);
                    
                    // Draw the border lines
                    Draw.Line(this, borderTag + "_Top", false, 
                        barsAgo, currentDFRHigh, 0, currentDFRHigh, 
                        BorderColor, BorderStyle, BorderThickness);
                    
                    Draw.Line(this, borderTag + "_Bottom", false, 
                        barsAgo, currentDFRLow, 0, currentDFRLow, 
                        BorderColor, BorderStyle, BorderThickness);
                    
                    Draw.Line(this, borderTag + "_Left", false, 
                        barsAgo, currentDFRHigh, barsAgo, currentDFRLow, 
                        BorderColor, BorderStyle, BorderThickness);
                    
                    Draw.Line(this, borderTag + "_Right", false, 
                        0, currentDFRHigh, 0, currentDFRLow, 
                        BorderColor, BorderStyle, BorderThickness);
                    
                    // Set this as the active DFR for projections
                    activeDFRHigh = currentDFRHigh;
                    activeDFRLow = currentDFRLow;
                    activeDFRStartBar = currentDFRStartBar;
                }
            }
            
            // Draw Fib projection lines from end of Monday DFR period to current bar
            if (activeDFRHigh != double.MinValue && activeDFRLow != double.MaxValue && activeDFRStartBar >= 0)
            {
                double dfrRange = activeDFRHigh - activeDFRLow;
                int barsFromDFRStart = CurrentBar - activeDFRStartBar;
                
                // Above levels (positive) - Bullish targets
                if (ShowLevel_Pos15)
                {
                    double level_pos15 = activeDFRHigh + (dfrRange * 0.5);
                    Draw.Line(this, "FibLevel_Pos15", false, barsFromDFRStart, level_pos15, 0, level_pos15, 
                        Level_Pos15_Color, Level_Pos15_Style, Level_Pos15_Thickness);
                }
                
                double level_pos20 = activeDFRHigh + (dfrRange * 1.0);
                Draw.Line(this, "FibLevel_Pos20", false, barsFromDFRStart, level_pos20, 0, level_pos20, 
                    Level_Pos20_Color, Level_Pos20_Style, Level_Pos20_Thickness);
                
                double level_pos25 = activeDFRHigh + (dfrRange * 1.5);
                Draw.Line(this, "FibLevel_Pos25", false, barsFromDFRStart, level_pos25, 0, level_pos25, 
                    Level_Pos25_Color, Level_Pos25_Style, Level_Pos25_Thickness);
                
                // Below levels (negative) - Bearish targets
                if (ShowLevel_Neg05)
                {
                    double level_neg05 = activeDFRLow - (dfrRange * 0.5);
                    Draw.Line(this, "FibLevel_Neg05", false, barsFromDFRStart, level_neg05, 0, level_neg05, 
                        Level_Neg05_Color, Level_Neg05_Style, Level_Neg05_Thickness);
                }
                
                double level_neg10 = activeDFRLow - (dfrRange * 1.0);
                Draw.Line(this, "FibLevel_Neg10", false, barsFromDFRStart, level_neg10, 0, level_neg10, 
                    Level_Neg10_Color, Level_Neg10_Style, Level_Neg10_Thickness);
                
                double level_neg15 = activeDFRLow - (dfrRange * 1.5);
                Draw.Line(this, "FibLevel_Neg15", false, barsFromDFRStart, level_neg15, 0, level_neg15, 
                    Level_Neg15_Color, Level_Neg15_Style, Level_Neg15_Thickness);
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Box Color", Description="Fill color of the DFR box", Order=1, GroupName="DFR Box Settings")]
        public Brush BoxColor { get; set; }

        [Browsable(false)]
        public string BoxColorSerializable
        {
            get { return Serialize.BrushToString(BoxColor); }
            set { BoxColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Box Opacity (%)", Description="Opacity of the box fill (0-100)", Order=2, GroupName="DFR Box Settings")]
        public int BoxOpacity { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="Border Color", Description="Color of the border line", Order=3, GroupName="DFR Box Settings")]
        public Brush BorderColor { get; set; }

        [Browsable(false)]
        public string BorderColorSerializable
        {
            get { return Serialize.BrushToString(BorderColor); }
            set { BorderColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Border Thickness", Description="Thickness of the border line", Order=4, GroupName="DFR Box Settings")]
        public int BorderThickness { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Border Style", Description="Dash style of the border line", Order=5, GroupName="DFR Box Settings")]
        public DashStyleHelper BorderStyle { get; set; }
        
        // Above Levels (Positive) - Bullish Targets
        [NinjaScriptProperty]
        [Display(Name="Show +1.5 Level", Description="Show the +1.5 fib level", Order=1, GroupName="Bullish Targets")]
        public bool ShowLevel_Pos15 { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="+1.5 Color", Description="Color of +1.5 level", Order=2, GroupName="Bullish Targets")]
        public Brush Level_Pos15_Color { get; set; }
        
        [Browsable(false)]
        public string Level_Pos15_ColorSerializable
        {
            get { return Serialize.BrushToString(Level_Pos15_Color); }
            set { Level_Pos15_Color = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="+1.5 Thickness", Order=3, GroupName="Bullish Targets")]
        public int Level_Pos15_Thickness { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="+1.5 Style", Order=4, GroupName="Bullish Targets")]
        public DashStyleHelper Level_Pos15_Style { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="+2.0 Color", Description="Color of +2.0 level", Order=5, GroupName="Bullish Targets")]
        public Brush Level_Pos20_Color { get; set; }
        
        [Browsable(false)]
        public string Level_Pos20_ColorSerializable
        {
            get { return Serialize.BrushToString(Level_Pos20_Color); }
            set { Level_Pos20_Color = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="+2.0 Thickness", Order=6, GroupName="Bullish Targets")]
        public int Level_Pos20_Thickness { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="+2.0 Style", Order=7, GroupName="Bullish Targets")]
        public DashStyleHelper Level_Pos20_Style { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="+2.5 Color", Description="Color of +2.5 level", Order=8, GroupName="Bullish Targets")]
        public Brush Level_Pos25_Color { get; set; }
        
        [Browsable(false)]
        public string Level_Pos25_ColorSerializable
        {
            get { return Serialize.BrushToString(Level_Pos25_Color); }
            set { Level_Pos25_Color = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="+2.5 Thickness", Order=9, GroupName="Bullish Targets")]
        public int Level_Pos25_Thickness { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="+2.5 Style", Order=10, GroupName="Bullish Targets")]
        public DashStyleHelper Level_Pos25_Style { get; set; }
        
        // Below Levels (Negative) - Bearish Targets
        [NinjaScriptProperty]
        [Display(Name="Show -0.5 Level", Description="Show the -0.5 fib level", Order=1, GroupName="Bearish Targets")]
        public bool ShowLevel_Neg05 { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="-0.5 Color", Description="Color of -0.5 level", Order=2, GroupName="Bearish Targets")]
        public Brush Level_Neg05_Color { get; set; }
        
        [Browsable(false)]
        public string Level_Neg05_ColorSerializable
        {
            get { return Serialize.BrushToString(Level_Neg05_Color); }
            set { Level_Neg05_Color = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="-0.5 Thickness", Order=3, GroupName="Bearish Targets")]
        public int Level_Neg05_Thickness { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="-0.5 Style", Order=4, GroupName="Bearish Targets")]
        public DashStyleHelper Level_Neg05_Style { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="-1.0 Color", Description="Color of -1.0 level", Order=5, GroupName="Bearish Targets")]
        public Brush Level_Neg10_Color { get; set; }
        
        [Browsable(false)]
        public string Level_Neg10_ColorSerializable
        {
            get { return Serialize.BrushToString(Level_Neg10_Color); }
            set { Level_Neg10_Color = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="-1.0 Thickness", Order=6, GroupName="Bearish Targets")]
        public int Level_Neg10_Thickness { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="-1.0 Style", Order=7, GroupName="Bearish Targets")]
        public DashStyleHelper Level_Neg10_Style { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name="-1.5 Color", Description="Color of -1.5 level", Order=8, GroupName="Bearish Targets")]
        public Brush Level_Neg15_Color { get; set; }
        
        [Browsable(false)]
        public string Level_Neg15_ColorSerializable
        {
            get { return Serialize.BrushToString(Level_Neg15_Color); }
            set { Level_Neg15_Color = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="-1.5 Thickness", Order=9, GroupName="Bearish Targets")]
        public int Level_Neg15_Thickness { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="-1.5 Style", Order=10, GroupName="Bearish Targets")]
        public DashStyleHelper Level_Neg15_Style { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private DFRWeek[] cacheDFRWeek;
		public DFRWeek DFRWeek(Brush boxColor, int boxOpacity, Brush borderColor, int borderThickness, DashStyleHelper borderStyle, bool showLevel_Pos15, Brush level_Pos15_Color, int level_Pos15_Thickness, DashStyleHelper level_Pos15_Style, Brush level_Pos20_Color, int level_Pos20_Thickness, DashStyleHelper level_Pos20_Style, Brush level_Pos25_Color, int level_Pos25_Thickness, DashStyleHelper level_Pos25_Style, bool showLevel_Neg05, Brush level_Neg05_Color, int level_Neg05_Thickness, DashStyleHelper level_Neg05_Style, Brush level_Neg10_Color, int level_Neg10_Thickness, DashStyleHelper level_Neg10_Style, Brush level_Neg15_Color, int level_Neg15_Thickness, DashStyleHelper level_Neg15_Style)
		{
			return DFRWeek(Input, boxColor, boxOpacity, borderColor, borderThickness, borderStyle, showLevel_Pos15, level_Pos15_Color, level_Pos15_Thickness, level_Pos15_Style, level_Pos20_Color, level_Pos20_Thickness, level_Pos20_Style, level_Pos25_Color, level_Pos25_Thickness, level_Pos25_Style, showLevel_Neg05, level_Neg05_Color, level_Neg05_Thickness, level_Neg05_Style, level_Neg10_Color, level_Neg10_Thickness, level_Neg10_Style, level_Neg15_Color, level_Neg15_Thickness, level_Neg15_Style);
		}

		public DFRWeek DFRWeek(ISeries<double> input, Brush boxColor, int boxOpacity, Brush borderColor, int borderThickness, DashStyleHelper borderStyle, bool showLevel_Pos15, Brush level_Pos15_Color, int level_Pos15_Thickness, DashStyleHelper level_Pos15_Style, Brush level_Pos20_Color, int level_Pos20_Thickness, DashStyleHelper level_Pos20_Style, Brush level_Pos25_Color, int level_Pos25_Thickness, DashStyleHelper level_Pos25_Style, bool showLevel_Neg05, Brush level_Neg05_Color, int level_Neg05_Thickness, DashStyleHelper level_Neg05_Style, Brush level_Neg10_Color, int level_Neg10_Thickness, DashStyleHelper level_Neg10_Style, Brush level_Neg15_Color, int level_Neg15_Thickness, DashStyleHelper level_Neg15_Style)
		{
			if (cacheDFRWeek != null)
				for (int idx = 0; idx < cacheDFRWeek.Length; idx++)
					if (cacheDFRWeek[idx] != null && cacheDFRWeek[idx].BoxColor == boxColor && cacheDFRWeek[idx].BoxOpacity == boxOpacity && cacheDFRWeek[idx].BorderColor == borderColor && cacheDFRWeek[idx].BorderThickness == borderThickness && cacheDFRWeek[idx].BorderStyle == borderStyle && cacheDFRWeek[idx].ShowLevel_Pos15 == showLevel_Pos15 && cacheDFRWeek[idx].Level_Pos15_Color == level_Pos15_Color && cacheDFRWeek[idx].Level_Pos15_Thickness == level_Pos15_Thickness && cacheDFRWeek[idx].Level_Pos15_Style == level_Pos15_Style && cacheDFRWeek[idx].Level_Pos20_Color == level_Pos20_Color && cacheDFRWeek[idx].Level_Pos20_Thickness == level_Pos20_Thickness && cacheDFRWeek[idx].Level_Pos20_Style == level_Pos20_Style && cacheDFRWeek[idx].Level_Pos25_Color == level_Pos25_Color && cacheDFRWeek[idx].Level_Pos25_Thickness == level_Pos25_Thickness && cacheDFRWeek[idx].Level_Pos25_Style == level_Pos25_Style && cacheDFRWeek[idx].ShowLevel_Neg05 == showLevel_Neg05 && cacheDFRWeek[idx].Level_Neg05_Color == level_Neg05_Color && cacheDFRWeek[idx].Level_Neg05_Thickness == level_Neg05_Thickness && cacheDFRWeek[idx].Level_Neg05_Style == level_Neg05_Style && cacheDFRWeek[idx].Level_Neg10_Color == level_Neg10_Color && cacheDFRWeek[idx].Level_Neg10_Thickness == level_Neg10_Thickness && cacheDFRWeek[idx].Level_Neg10_Style == level_Neg10_Style && cacheDFRWeek[idx].Level_Neg15_Color == level_Neg15_Color && cacheDFRWeek[idx].Level_Neg15_Thickness == level_Neg15_Thickness && cacheDFRWeek[idx].Level_Neg15_Style == level_Neg15_Style && cacheDFRWeek[idx].EqualsInput(input))
						return cacheDFRWeek[idx];
			return CacheIndicator<DFRWeek>(new DFRWeek(){ BoxColor = boxColor, BoxOpacity = boxOpacity, BorderColor = borderColor, BorderThickness = borderThickness, BorderStyle = borderStyle, ShowLevel_Pos15 = showLevel_Pos15, Level_Pos15_Color = level_Pos15_Color, Level_Pos15_Thickness = level_Pos15_Thickness, Level_Pos15_Style = level_Pos15_Style, Level_Pos20_Color = level_Pos20_Color, Level_Pos20_Thickness = level_Pos20_Thickness, Level_Pos20_Style = level_Pos20_Style, Level_Pos25_Color = level_Pos25_Color, Level_Pos25_Thickness = level_Pos25_Thickness, Level_Pos25_Style = level_Pos25_Style, ShowLevel_Neg05 = showLevel_Neg05, Level_Neg05_Color = level_Neg05_Color, Level_Neg05_Thickness = level_Neg05_Thickness, Level_Neg05_Style = level_Neg05_Style, Level_Neg10_Color = level_Neg10_Color, Level_Neg10_Thickness = level_Neg10_Thickness, Level_Neg10_Style = level_Neg10_Style, Level_Neg15_Color = level_Neg15_Color, Level_Neg15_Thickness = level_Neg15_Thickness, Level_Neg15_Style = level_Neg15_Style }, input, ref cacheDFRWeek);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.DFRWeek DFRWeek(Brush boxColor, int boxOpacity, Brush borderColor, int borderThickness, DashStyleHelper borderStyle, bool showLevel_Pos15, Brush level_Pos15_Color, int level_Pos15_Thickness, DashStyleHelper level_Pos15_Style, Brush level_Pos20_Color, int level_Pos20_Thickness, DashStyleHelper level_Pos20_Style, Brush level_Pos25_Color, int level_Pos25_Thickness, DashStyleHelper level_Pos25_Style, bool showLevel_Neg05, Brush level_Neg05_Color, int level_Neg05_Thickness, DashStyleHelper level_Neg05_Style, Brush level_Neg10_Color, int level_Neg10_Thickness, DashStyleHelper level_Neg10_Style, Brush level_Neg15_Color, int level_Neg15_Thickness, DashStyleHelper level_Neg15_Style)
		{
			return indicator.DFRWeek(Input, boxColor, boxOpacity, borderColor, borderThickness, borderStyle, showLevel_Pos15, level_Pos15_Color, level_Pos15_Thickness, level_Pos15_Style, level_Pos20_Color, level_Pos20_Thickness, level_Pos20_Style, level_Pos25_Color, level_Pos25_Thickness, level_Pos25_Style, showLevel_Neg05, level_Neg05_Color, level_Neg05_Thickness, level_Neg05_Style, level_Neg10_Color, level_Neg10_Thickness, level_Neg10_Style, level_Neg15_Color, level_Neg15_Thickness, level_Neg15_Style);
		}

		public Indicators.DFRWeek DFRWeek(ISeries<double> input , Brush boxColor, int boxOpacity, Brush borderColor, int borderThickness, DashStyleHelper borderStyle, bool showLevel_Pos15, Brush level_Pos15_Color, int level_Pos15_Thickness, DashStyleHelper level_Pos15_Style, Brush level_Pos20_Color, int level_Pos20_Thickness, DashStyleHelper level_Pos20_Style, Brush level_Pos25_Color, int level_Pos25_Thickness, DashStyleHelper level_Pos25_Style, bool showLevel_Neg05, Brush level_Neg05_Color, int level_Neg05_Thickness, DashStyleHelper level_Neg05_Style, Brush level_Neg10_Color, int level_Neg10_Thickness, DashStyleHelper level_Neg10_Style, Brush level_Neg15_Color, int level_Neg15_Thickness, DashStyleHelper level_Neg15_Style)
		{
			return indicator.DFRWeek(input, boxColor, boxOpacity, borderColor, borderThickness, borderStyle, showLevel_Pos15, level_Pos15_Color, level_Pos15_Thickness, level_Pos15_Style, level_Pos20_Color, level_Pos20_Thickness, level_Pos20_Style, level_Pos25_Color, level_Pos25_Thickness, level_Pos25_Style, showLevel_Neg05, level_Neg05_Color, level_Neg05_Thickness, level_Neg05_Style, level_Neg10_Color, level_Neg10_Thickness, level_Neg10_Style, level_Neg15_Color, level_Neg15_Thickness, level_Neg15_Style);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.DFRWeek DFRWeek(Brush boxColor, int boxOpacity, Brush borderColor, int borderThickness, DashStyleHelper borderStyle, bool showLevel_Pos15, Brush level_Pos15_Color, int level_Pos15_Thickness, DashStyleHelper level_Pos15_Style, Brush level_Pos20_Color, int level_Pos20_Thickness, DashStyleHelper level_Pos20_Style, Brush level_Pos25_Color, int level_Pos25_Thickness, DashStyleHelper level_Pos25_Style, bool showLevel_Neg05, Brush level_Neg05_Color, int level_Neg05_Thickness, DashStyleHelper level_Neg05_Style, Brush level_Neg10_Color, int level_Neg10_Thickness, DashStyleHelper level_Neg10_Style, Brush level_Neg15_Color, int level_Neg15_Thickness, DashStyleHelper level_Neg15_Style)
		{
			return indicator.DFRWeek(Input, boxColor, boxOpacity, borderColor, borderThickness, borderStyle, showLevel_Pos15, level_Pos15_Color, level_Pos15_Thickness, level_Pos15_Style, level_Pos20_Color, level_Pos20_Thickness, level_Pos20_Style, level_Pos25_Color, level_Pos25_Thickness, level_Pos25_Style, showLevel_Neg05, level_Neg05_Color, level_Neg05_Thickness, level_Neg05_Style, level_Neg10_Color, level_Neg10_Thickness, level_Neg10_Style, level_Neg15_Color, level_Neg15_Thickness, level_Neg15_Style);
		}

		public Indicators.DFRWeek DFRWeek(ISeries<double> input , Brush boxColor, int boxOpacity, Brush borderColor, int borderThickness, DashStyleHelper borderStyle, bool showLevel_Pos15, Brush level_Pos15_Color, int level_Pos15_Thickness, DashStyleHelper level_Pos15_Style, Brush level_Pos20_Color, int level_Pos20_Thickness, DashStyleHelper level_Pos20_Style, Brush level_Pos25_Color, int level_Pos25_Thickness, DashStyleHelper level_Pos25_Style, bool showLevel_Neg05, Brush level_Neg05_Color, int level_Neg05_Thickness, DashStyleHelper level_Neg05_Style, Brush level_Neg10_Color, int level_Neg10_Thickness, DashStyleHelper level_Neg10_Style, Brush level_Neg15_Color, int level_Neg15_Thickness, DashStyleHelper level_Neg15_Style)
		{
			return indicator.DFRWeek(input, boxColor, boxOpacity, borderColor, borderThickness, borderStyle, showLevel_Pos15, level_Pos15_Color, level_Pos15_Thickness, level_Pos15_Style, level_Pos20_Color, level_Pos20_Thickness, level_Pos20_Style, level_Pos25_Color, level_Pos25_Thickness, level_Pos25_Style, showLevel_Neg05, level_Neg05_Color, level_Neg05_Thickness, level_Neg05_Style, level_Neg10_Color, level_Neg10_Thickness, level_Neg10_Style, level_Neg15_Color, level_Neg15_Thickness, level_Neg15_Style);
		}
	}
}

#endregion
