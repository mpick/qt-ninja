#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class SMTWeeklyCycle : Indicator
    {
        private Series<double> comparisonSeries;
        private double priorWeekHigh;
        private double priorWeekLow;
        private double compPriorWeekHigh;
        private double compPriorWeekLow;
        private double comp2PriorWeekHigh;
        private double comp2PriorWeekLow;
        private DateTime priorWeekHighTime;
        private DateTime priorWeekLowTime;
        private DateTime lastWeekStart;
        private bool primaryBrokeHigh;
        private bool comparisonBrokeHigh;
        private bool comparison2BrokeHigh;
        private bool primaryBrokeLow;
        private bool comparisonBrokeLow;
        private bool comparison2BrokeLow;
        private bool useSecondComparison;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Detects SMT divergences between instruments on a weekly basis (Sunday 18:00 to Friday 16:59)";
                Name = "SSMT Monthly Cycle";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                ComparisonTicker = "SI 12-25";
                ComparisonTicker2 = "PL 01-26";
                BearishSMTColor = Brushes.SteelBlue;
                BullishSMTColor = Brushes.SteelBlue;
                LineWidth = 2;
            }
            else if (State == State.Configure)
            {
                // Use the same bar period as the primary chart
                AddDataSeries(ComparisonTicker);
                
                // Add second comparison if specified
                useSecondComparison = !string.IsNullOrEmpty(ComparisonTicker2);
                if (useSecondComparison)
                {
                    AddDataSeries(ComparisonTicker2);
                }
            }
            else if (State == State.DataLoaded)
            {
                comparisonSeries = new Series<double>(this);
                priorWeekHigh = double.MinValue;
                priorWeekLow = double.MaxValue;
                compPriorWeekHigh = double.MinValue;
                compPriorWeekLow = double.MaxValue;
                comp2PriorWeekHigh = double.MinValue;
                comp2PriorWeekLow = double.MaxValue;
                lastWeekStart = DateTime.MinValue;
                priorWeekHighTime = DateTime.MinValue;
                priorWeekLowTime = DateTime.MinValue;
            }
        }

        protected override void OnBarUpdate()
        {
            int maxBars = useSecondComparison ? 3 : 2;
            if (CurrentBars[0] < 2 || CurrentBars[1] < 2)
                return;
            if (useSecondComparison && CurrentBars[2] < 2)
                return;
            
            // Check if we're on a new trading week (Sunday 18:00 EST = week start)
            DateTime currentWeekStart = GetTradingWeekStart(Time[0]);
            
            if (currentWeekStart != lastWeekStart && lastWeekStart != DateTime.MinValue)
            {
                // New week - reset for new week
                priorWeekHigh = GetPriorWeekHigh(0);
                priorWeekLow = GetPriorWeekLow(0);
                compPriorWeekHigh = GetPriorWeekHigh(1);
                compPriorWeekLow = GetPriorWeekLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorWeekHigh = GetPriorWeekHigh(2);
                    comp2PriorWeekLow = GetPriorWeekLow(2);
                }
                
                // Store the TIME of the prior week high/low
                priorWeekHighTime = GetPriorWeekHighTime();
                priorWeekLowTime = GetPriorWeekLowTime();
                
                primaryBrokeHigh = false;
                comparisonBrokeHigh = false;
                comparison2BrokeHigh = false;
                primaryBrokeLow = false;
                comparisonBrokeLow = false;
                comparison2BrokeLow = false;
            }
            
            if (lastWeekStart == DateTime.MinValue)
            {
                // First run - initialize prior week values
                priorWeekHigh = GetPriorWeekHigh(0);
                priorWeekLow = GetPriorWeekLow(0);
                compPriorWeekHigh = GetPriorWeekHigh(1);
                compPriorWeekLow = GetPriorWeekLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorWeekHigh = GetPriorWeekHigh(2);
                    comp2PriorWeekLow = GetPriorWeekLow(2);
                }
                
                priorWeekHighTime = GetPriorWeekHighTime();
                priorWeekLowTime = GetPriorWeekLowTime();
            }
            
            lastWeekStart = currentWeekStart;
            
            // Check for breaks on current bar
            // Primary instrument (GC)
            if (BarsInProgress == 0)
            {
                if (High[0] > priorWeekHigh && !primaryBrokeHigh)
                {
                    primaryBrokeHigh = true;
                    CheckForSMTOnBreak(true, 0); // high break, primary
                }
                
                if (Low[0] < priorWeekLow && !primaryBrokeLow)
                {
                    primaryBrokeLow = true;
                    CheckForSMTOnBreak(false, 0); // low break, primary
                }
            }
            
            // Comparison instrument (SI)
            if (CurrentBars[1] >= 1)
            {
                if (Highs[1][0] > compPriorWeekHigh && !comparisonBrokeHigh)
                {
                    comparisonBrokeHigh = true;
                    CheckForSMTOnBreak(true, 1); // high break, comparison 1
                }
                
                if (Lows[1][0] < compPriorWeekLow && !comparisonBrokeLow)
                {
                    comparisonBrokeLow = true;
                    CheckForSMTOnBreak(false, 1); // low break, comparison 1
                }
            }
            
            // Second comparison instrument (PL) - if enabled
            if (useSecondComparison && CurrentBars[2] >= 1)
            {
                if (Highs[2][0] > comp2PriorWeekHigh && !comparison2BrokeHigh)
                {
                    comparison2BrokeHigh = true;
                    CheckForSMTOnBreak(true, 2); // high break, comparison 2
                }
                
                if (Lows[2][0] < comp2PriorWeekLow && !comparison2BrokeLow)
                {
                    comparison2BrokeLow = true;
                    CheckForSMTOnBreak(false, 2); // low break, comparison 2
                }
            }
        }
        
        private void CheckForSMTOnBreak(bool isHighBreak, int whichInstrument)
        {
            // whichInstrument: 0 = primary (GC), 1 = comparison 1 (SI), 2 = comparison 2 (PL)
            
            if (isHighBreak)
            {
                // Bearish SMT - one broke high, check if primary hasn't
                if (whichInstrument > 0 && !primaryBrokeHigh)
                {
                    // One of the comparison instruments broke high, primary didn't
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " MCSSMT";
                    DrawSMTLine(true, priorWeekHighTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeHigh && (!useSecondComparison || !comparison2BrokeHigh))
                {
                    // Primary broke high, but none of the comparison instruments did
                    DrawSMTLine(true, priorWeekHighTime, false, ""); // false = no label
                }
            }
            else
            {
                // Bullish SMT - one broke low, check if primary hasn't
                if (whichInstrument > 0 && !primaryBrokeLow)
                {
                    // One of the comparison instruments broke low, primary didn't
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " MCSSMT";
                    DrawSMTLine(false, priorWeekLowTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeLow && (!useSecondComparison || !comparison2BrokeLow))
                {
                    // Primary broke low, but none of the comparison instruments did
                    DrawSMTLine(false, priorWeekLowTime, false, ""); // false = no label
                }
            }
        }
        
        private void DrawSMTLine(bool isBearish, DateTime startTime, bool showLabel, string labelTicker)
        {
            if (startTime == DateTime.MinValue)
                return;
            
            // Find the bar index for the start time
            int startBar = -1;
            for (int i = 0; i < Math.Min(CurrentBar, 2000); i++)
            {
                if (Time[i] == startTime)
                {
                    startBar = i;
                    break;
                }
            }
            
            if (startBar < 0)
                return;
            
            string tag = "SMTWeek_" + (isBearish ? "Bear_" : "Bull_") + Time[0].ToString("yyyyMMddHHmmss");
            
            if (isBearish)
            {
                // Draw from prior week high to current candle high
                Draw.Line(this, tag, false, 
                    startBar, High[startBar], 
                    0, High[0], 
                    BearishSMTColor, DashStyleHelper.Solid, LineWidth);
                
                // Add label if comparison asset caused the SMT (above the line for bearish)
                if (showLabel)
                {
                    int midBar = startBar / 2;
                    double midPrice = (High[startBar] + High[0]) / 2;
                    
                    // Extract just the symbol (e.g., "SI" from "SI 12-25")
                    string symbol = labelTicker.Split(' ')[0] + "-" + labelTicker.Split(' ')[1];
                    
                    Draw.Text(this, tag + "_Label", symbol, midBar, midPrice + (20 * TickSize), BearishSMTColor);
                }
            }
            else
            {
                // Draw from prior week low to current candle low
                Draw.Line(this, tag, false, 
                    startBar, Low[startBar], 
                    0, Low[0], 
                    BullishSMTColor, DashStyleHelper.Solid, LineWidth);
                
                // Add label if comparison asset caused the SMT (below the line for bullish)
                if (showLabel)
                {
                    int midBar = startBar / 2;
                    double midPrice = (Low[startBar] + Low[0]) / 2;
                    
                    // Extract just the symbol (e.g., "SI" from "SI 12-25")
                    string symbol = labelTicker.Split(' ')[0] + "-" + labelTicker.Split(' ')[2];
                    
                    Draw.Text(this, tag + "_Label", symbol, midBar, midPrice - (20 * TickSize), BullishSMTColor);
                }
            }
        }
        
        private DateTime GetTradingWeekStart(DateTime time)
        {
            // Trading week starts Sunday 18:00 EST and ends Friday 16:59 EST
            // Find the most recent Sunday 18:00
            
            DateTime current = time;
            
            // If it's before Sunday 18:00 or it's Friday after 17:00 to Sunday before 18:00
            // we're in the previous week
            
            int daysToSubtract = 0;
            
            if (current.DayOfWeek == DayOfWeek.Sunday)
            {
                // If Sunday before 18:00, go back to previous Sunday
                if (current.TimeOfDay < new TimeSpan(18, 0, 0))
                    daysToSubtract = 7;
            }
            else if (current.DayOfWeek == DayOfWeek.Monday)
                daysToSubtract = 1;
            else if (current.DayOfWeek == DayOfWeek.Tuesday)
                daysToSubtract = 2;
            else if (current.DayOfWeek == DayOfWeek.Wednesday)
                daysToSubtract = 3;
            else if (current.DayOfWeek == DayOfWeek.Thursday)
                daysToSubtract = 4;
            else if (current.DayOfWeek == DayOfWeek.Friday)
                daysToSubtract = 5;
            else if (current.DayOfWeek == DayOfWeek.Saturday)
                daysToSubtract = 6;
            
            DateTime weekStart = current.Date.AddDays(-daysToSubtract).Add(new TimeSpan(18, 0, 0));
            
            // If current time is before the calculated week start, go back one more week
            if (time < weekStart)
                weekStart = weekStart.AddDays(-7);
            
            return weekStart;
        }
        
        private double GetPriorWeekHigh(int barsSeriesIndex)
        {
            double high = double.MinValue;
            DateTime currentWeekStart = GetTradingWeekStart(Times[barsSeriesIndex][0]);
            DateTime priorWeekStart = currentWeekStart.AddDays(-7);
            DateTime priorWeekEnd = currentWeekStart;
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 2000); i++)
            {
                DateTime barTime = Times[barsSeriesIndex][i];
                
                if (barTime < priorWeekStart)
                    break;
                
                if (barTime >= priorWeekStart && barTime < priorWeekEnd)
                {
                    if (barsSeriesIndex == 0)
                        high = Math.Max(high, High[i]);
                    else
                        high = Math.Max(high, Highs[barsSeriesIndex][i]);
                }
            }
            
            return high;
        }
        
        private double GetPriorWeekLow(int barsSeriesIndex)
        {
            double low = double.MaxValue;
            DateTime currentWeekStart = GetTradingWeekStart(Times[barsSeriesIndex][0]);
            DateTime priorWeekStart = currentWeekStart.AddDays(-7);
            DateTime priorWeekEnd = currentWeekStart;
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 2000); i++)
            {
                DateTime barTime = Times[barsSeriesIndex][i];
                
                if (barTime < priorWeekStart)
                    break;
                
                if (barTime >= priorWeekStart && barTime < priorWeekEnd)
                {
                    if (barsSeriesIndex == 0)
                        low = Math.Min(low, Low[i]);
                    else
                        low = Math.Min(low, Lows[barsSeriesIndex][i]);
                }
            }
            
            return low;
        }
        
        private DateTime GetPriorWeekHighTime()
        {
            double targetHigh = priorWeekHigh;
            if (targetHigh == double.MinValue)
                return DateTime.MinValue;
            
            DateTime currentWeekStart = GetTradingWeekStart(Time[0]);
            DateTime priorWeekStart = currentWeekStart.AddDays(-7);
            DateTime priorWeekEnd = currentWeekStart;
            
            // Search backwards for the bar that made the prior week high
            for (int i = 1; i < Math.Min(CurrentBar, 2000); i++)
            {
                DateTime barTime = Time[i];
                
                if (barTime < priorWeekStart)
                    break;
                
                if (barTime >= priorWeekStart && barTime < priorWeekEnd && Math.Abs(High[i] - targetHigh) < 0.01)
                {
                    return Time[i];
                }
            }
            
            return DateTime.MinValue;
        }
        
        private DateTime GetPriorWeekLowTime()
        {
            double targetLow = priorWeekLow;
            if (targetLow == double.MaxValue)
                return DateTime.MinValue;
            
            DateTime currentWeekStart = GetTradingWeekStart(Time[0]);
            DateTime priorWeekStart = currentWeekStart.AddDays(-7);
            DateTime priorWeekEnd = currentWeekStart;
            
            // Search backwards for the bar that made the prior week low
            for (int i = 1; i < Math.Min(CurrentBar, 2000); i++)
            {
                DateTime barTime = Time[i];
                
                if (barTime < priorWeekStart)
                    break;
                
                if (barTime >= priorWeekStart && barTime < priorWeekEnd && Math.Abs(Low[i] - targetLow) < 0.01)
                {
                    return Time[i];
                }
            }
            
            return DateTime.MinValue;
        }
        
        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Comparison Ticker", Description = "First comparison ticker symbol (e.g., SI 12-25)", Order = 1, GroupName = "Parameters")]
        public string ComparisonTicker { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Comparison Ticker 2 (Optional)", Description = "Second comparison ticker symbol (e.g., PL 01-26). Leave empty to compare only 2 assets.", Order = 2, GroupName = "Parameters")]
        public string ComparisonTicker2 { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bearish SMT Color", Description = "Color for bearish SMT lines (high breaks)", Order = 3, GroupName = "Parameters")]
        public Brush BearishSMTColor { get; set; }
        
        [Browsable(false)]
        public string BearishSMTColorSerializable
        {
            get { return Serialize.BrushToString(BearishSMTColor); }
            set { BearishSMTColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bullish SMT Color", Description = "Color for bullish SMT lines (low breaks)", Order = 4, GroupName = "Parameters")]
        public Brush BullishSMTColor { get; set; }
        
        [Browsable(false)]
        public string BullishSMTColorSerializable
        {
            get { return Serialize.BrushToString(BullishSMTColor); }
            set { BullishSMTColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Line Width", Description = "Width of SMT trendlines", Order = 5, GroupName = "Parameters")]
        public int LineWidth { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SMTWeeklyCycle[] cacheSMTWeeklyCycle;
		public SMTWeeklyCycle SMTWeeklyCycle(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return SMTWeeklyCycle(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public SMTWeeklyCycle SMTWeeklyCycle(ISeries<double> input, string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			if (cacheSMTWeeklyCycle != null)
				for (int idx = 0; idx < cacheSMTWeeklyCycle.Length; idx++)
					if (cacheSMTWeeklyCycle[idx] != null && cacheSMTWeeklyCycle[idx].ComparisonTicker == comparisonTicker && cacheSMTWeeklyCycle[idx].ComparisonTicker2 == comparisonTicker2 && cacheSMTWeeklyCycle[idx].BearishSMTColor == bearishSMTColor && cacheSMTWeeklyCycle[idx].BullishSMTColor == bullishSMTColor && cacheSMTWeeklyCycle[idx].LineWidth == lineWidth && cacheSMTWeeklyCycle[idx].EqualsInput(input))
						return cacheSMTWeeklyCycle[idx];
			return CacheIndicator<SMTWeeklyCycle>(new SMTWeeklyCycle(){ ComparisonTicker = comparisonTicker, ComparisonTicker2 = comparisonTicker2, BearishSMTColor = bearishSMTColor, BullishSMTColor = bullishSMTColor, LineWidth = lineWidth }, input, ref cacheSMTWeeklyCycle);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SMTWeeklyCycle SMTWeeklyCycle(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTWeeklyCycle(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMTWeeklyCycle SMTWeeklyCycle(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTWeeklyCycle(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SMTWeeklyCycle SMTWeeklyCycle(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTWeeklyCycle(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMTWeeklyCycle SMTWeeklyCycle(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTWeeklyCycle(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

#endregion
