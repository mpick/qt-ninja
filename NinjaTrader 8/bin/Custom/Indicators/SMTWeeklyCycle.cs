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
    public class SMTDivergence : Indicator
    {
        private Series<double> comparisonSeries;
        private double priorDayHigh;
        private double priorDayLow;
        private double compPriorDayHigh;
        private double compPriorDayLow;
        private double comp2PriorDayHigh;
        private double comp2PriorDayLow;
        private DateTime priorDayHighTime;
        private DateTime priorDayLowTime;
        private DateTime lastSessionDate;
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
                Description = @"Detects SMT divergences between primary and comparison instruments";
                Name = "SSMT Weekly Cycle";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                ComparisonTicker = "SI 12-25";
                ComparisonTicker2 = "PL 01-26";
                BearishSMTColor = Brushes.Red;
                BullishSMTColor = Brushes.Lime;
                LineWidth = 2;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(ComparisonTicker, BarsPeriodType.Minute, 60, MarketDataType.Last);
                
                // Add second comparison if specified
                useSecondComparison = !string.IsNullOrEmpty(ComparisonTicker2);
                if (useSecondComparison)
                {
                    AddDataSeries(ComparisonTicker2, BarsPeriodType.Minute, 60, MarketDataType.Last);
                }
            }
            else if (State == State.DataLoaded)
            {
                comparisonSeries = new Series<double>(this);
                priorDayHigh = double.MinValue;
                priorDayLow = double.MaxValue;
                compPriorDayHigh = double.MinValue;
                compPriorDayLow = double.MaxValue;
                comp2PriorDayHigh = double.MinValue;
                comp2PriorDayLow = double.MaxValue;
                lastSessionDate = DateTime.MinValue;
                priorDayHighTime = DateTime.MinValue;
                priorDayLowTime = DateTime.MinValue;
            }
        }

        protected override void OnBarUpdate()
        {
            int maxBars = useSecondComparison ? 3 : 2;
            if (CurrentBars[0] < 2 || CurrentBars[1] < 2)
                return;
            if (useSecondComparison && CurrentBars[2] < 2)
                return;
            
            // Check if we're on a new trading session (18:00 EST = session start)
            DateTime currentSessionDate = GetTradingDay(Time[0]);
            
            if (currentSessionDate != lastSessionDate && lastSessionDate != DateTime.MinValue)
            {
                // New session - reset for new session
                priorDayHigh = GetPriorSessionHigh(0);
                priorDayLow = GetPriorSessionLow(0);
                compPriorDayHigh = GetPriorSessionHigh(1);
                compPriorDayLow = GetPriorSessionLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorDayHigh = GetPriorSessionHigh(2);
                    comp2PriorDayLow = GetPriorSessionLow(2);
                }
                
                // Store the TIME of the prior day high/low
                priorDayHighTime = GetPriorDayHighTime();
                priorDayLowTime = GetPriorDayLowTime();
                
                primaryBrokeHigh = false;
                comparisonBrokeHigh = false;
                comparison2BrokeHigh = false;
                primaryBrokeLow = false;
                comparisonBrokeLow = false;
                comparison2BrokeLow = false;
            }
            
            if (lastSessionDate == DateTime.MinValue)
            {
                // First run - initialize prior day values
                priorDayHigh = GetPriorSessionHigh(0);
                priorDayLow = GetPriorSessionLow(0);
                compPriorDayHigh = GetPriorSessionHigh(1);
                compPriorDayLow = GetPriorSessionLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorDayHigh = GetPriorSessionHigh(2);
                    comp2PriorDayLow = GetPriorSessionLow(2);
                }
                
                priorDayHighTime = GetPriorDayHighTime();
                priorDayLowTime = GetPriorDayLowTime();
            }
            
            lastSessionDate = currentSessionDate;
            
            // Check for breaks on current bar - always check both instruments
            // Primary instrument (GC)
            if (BarsInProgress == 0)
            {
                if (High[0] > priorDayHigh && !primaryBrokeHigh)
                {
                    primaryBrokeHigh = true;
                    CheckForSMTOnBreak(true, 0); // high break, primary
                }
                
                if (Low[0] < priorDayLow && !primaryBrokeLow)
                {
                    primaryBrokeLow = true;
                    CheckForSMTOnBreak(false, 0); // low break, primary
                }
            }
            
            // Comparison instrument (SI) - check on both BarsInProgress
            if (CurrentBars[1] >= 1)
            {
                if (Highs[1][0] > compPriorDayHigh && !comparisonBrokeHigh)
                {
                    comparisonBrokeHigh = true;
                    CheckForSMTOnBreak(true, 1); // high break, comparison 1
                }
                
                if (Lows[1][0] < compPriorDayLow && !comparisonBrokeLow)
                {
                    comparisonBrokeLow = true;
                    CheckForSMTOnBreak(false, 1); // low break, comparison 1
                }
            }
            
            // Second comparison instrument (PL) - if enabled
            if (useSecondComparison && CurrentBars[2] >= 1)
            {
                if (Highs[2][0] > comp2PriorDayHigh && !comparison2BrokeHigh)
                {
                    comparison2BrokeHigh = true;
                    CheckForSMTOnBreak(true, 2); // high break, comparison 2
                }
                
                if (Lows[2][0] < comp2PriorDayLow && !comparison2BrokeLow)
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
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " WCSSMT";
                    DrawSMTLine(true, priorDayHighTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeHigh && (!useSecondComparison || !comparison2BrokeHigh))
                {
                    // Primary broke high, but none of the comparison instruments did
                    DrawSMTLine(true, priorDayHighTime, false, ""); // false = no label
                }
            }
            else
            {
                // Bullish SMT - one broke low, check if primary hasn't
                if (whichInstrument > 0 && !primaryBrokeLow)
                {
                    // One of the comparison instruments broke low, primary didn't
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " WCSSMT";
                    DrawSMTLine(false, priorDayLowTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeLow && (!useSecondComparison || !comparison2BrokeLow))
                {
                    // Primary broke low, but none of the comparison instruments did
                    DrawSMTLine(false, priorDayLowTime, false, ""); // false = no label
                }
            }
        }
        
        private void DrawSMTLine(bool isBearish, DateTime startTime, bool showLabel, string labelTicker)
        {
            if (startTime == DateTime.MinValue)
                return;
            
            // Find the bar index for the start time
            int startBar = -1;
            for (int i = 0; i < Math.Min(CurrentBar, 500); i++)
            {
                if (Time[i] == startTime)
                {
                    startBar = i;
                    break;
                }
            }
            
            if (startBar < 0)
                return;
            
            string tag = "SMT_" + (isBearish ? "Bear_" : "Bull_") + Time[0].ToString("yyyyMMddHHmmss");
            
            if (isBearish)
            {
                // Draw from prior day high to current candle high
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
                // Draw from prior day low to current candle low
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
        
        private DateTime GetTradingDay(DateTime time)
        {
            // Trading day starts at 18:00 EST (6:00 PM)
            // If before 18:00, it's part of previous day's session
            TimeSpan sessionStart = new TimeSpan(18, 0, 0);
            
            if (time.TimeOfDay < sessionStart)
                return time.Date.AddDays(-1);
            else
                return time.Date;
        }
        
        private double GetPriorSessionHigh(int barsSeriesIndex)
        {
            double high = double.MinValue;
            DateTime currentSession = GetTradingDay(Times[barsSeriesIndex][0]);
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 500); i++)
            {
                DateTime barSession = GetTradingDay(Times[barsSeriesIndex][i]);
                
                if (barSession < currentSession.AddDays(-1))
                    break;
                
                if (barSession == currentSession.AddDays(-1))
                {
                    if (barsSeriesIndex == 0)
                        high = Math.Max(high, High[i]);
                    else
                        high = Math.Max(high, Highs[barsSeriesIndex][i]);
                }
            }
            
            return high;
        }
        
        private double GetPriorSessionLow(int barsSeriesIndex)
        {
            double low = double.MaxValue;
            DateTime currentSession = GetTradingDay(Times[barsSeriesIndex][0]);
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 500); i++)
            {
                DateTime barSession = GetTradingDay(Times[barsSeriesIndex][i]);
                
                if (barSession < currentSession.AddDays(-1))
                    break;
                
                if (barSession == currentSession.AddDays(-1))
                {
                    if (barsSeriesIndex == 0)
                        low = Math.Min(low, Low[i]);
                    else
                        low = Math.Min(low, Lows[barsSeriesIndex][i]);
                }
            }
            
            return low;
        }
        
        private DateTime GetPriorDayHighTime()
        {
            double targetHigh = priorDayHigh;
            if (targetHigh == double.MinValue)
                return DateTime.MinValue;
            
            DateTime currentSession = GetTradingDay(Time[0]);
            DateTime priorSession = currentSession.AddDays(-1);
            
            // Search backwards for the bar that made the prior day high
            for (int i = 1; i < Math.Min(CurrentBar, 500); i++)
            {
                DateTime barSession = GetTradingDay(Time[i]);
                
                if (barSession < priorSession)
                    break;
                
                if (barSession == priorSession && Math.Abs(High[i] - targetHigh) < 0.01)
                {
                    return Time[i];
                }
            }
            
            return DateTime.MinValue;
        }
        
        private DateTime GetPriorDayLowTime()
        {
            double targetLow = priorDayLow;
            if (targetLow == double.MaxValue)
                return DateTime.MinValue;
            
            DateTime currentSession = GetTradingDay(Time[0]);
            DateTime priorSession = currentSession.AddDays(-1);
            
            // Search backwards for the bar that made the prior day low
            for (int i = 1; i < Math.Min(CurrentBar, 500); i++)
            {
                DateTime barSession = GetTradingDay(Time[i]);
                
                if (barSession < priorSession)
                    break;
                
                if (barSession == priorSession && Math.Abs(Low[i] - targetLow) < 0.01)
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
		private SMTDivergence[] cacheSMTDivergence;
		public SMTDivergence SMTDivergence(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return SMTDivergence(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public SMTDivergence SMTDivergence(ISeries<double> input, string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			if (cacheSMTDivergence != null)
				for (int idx = 0; idx < cacheSMTDivergence.Length; idx++)
					if (cacheSMTDivergence[idx] != null && cacheSMTDivergence[idx].ComparisonTicker == comparisonTicker && cacheSMTDivergence[idx].ComparisonTicker2 == comparisonTicker2 && cacheSMTDivergence[idx].BearishSMTColor == bearishSMTColor && cacheSMTDivergence[idx].BullishSMTColor == bullishSMTColor && cacheSMTDivergence[idx].LineWidth == lineWidth && cacheSMTDivergence[idx].EqualsInput(input))
						return cacheSMTDivergence[idx];
			return CacheIndicator<SMTDivergence>(new SMTDivergence(){ ComparisonTicker = comparisonTicker, ComparisonTicker2 = comparisonTicker2, BearishSMTColor = bearishSMTColor, BullishSMTColor = bullishSMTColor, LineWidth = lineWidth }, input, ref cacheSMTDivergence);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SMTDivergence SMTDivergence(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTDivergence(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMTDivergence SMTDivergence(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTDivergence(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SMTDivergence SMTDivergence(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTDivergence(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMTDivergence SMTDivergence(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTDivergence(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

#endregion
