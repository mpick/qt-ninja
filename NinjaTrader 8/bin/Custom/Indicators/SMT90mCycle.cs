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
    public class SMT90mCycle : Indicator
    {
        private Series<double> comparisonSeries;
        private double priorCycleHigh;
        private double priorCycleLow;
        private double compPriorCycleHigh;
        private double compPriorCycleLow;
        private double comp2PriorCycleHigh;
        private double comp2PriorCycleLow;
        private DateTime priorCycleHighTime;
        private DateTime priorCycleLowTime;
        private int lastCycle;
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
                Description = @"Detects SMT divergences between 90-minute cycles within each 6-hour session";
                Name = "SSMT 90m Cycle";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                ComparisonTicker = "SI 12-25";
                ComparisonTicker2 = "PL 01-26";
                BearishSMTColor = Brushes.Black;
                BullishSMTColor = Brushes.Black;
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
                priorCycleHigh = double.MinValue;
                priorCycleLow = double.MaxValue;
                compPriorCycleHigh = double.MinValue;
                compPriorCycleLow = double.MaxValue;
                comp2PriorCycleHigh = double.MinValue;
                comp2PriorCycleLow = double.MaxValue;
                lastCycle = -1;
                priorCycleHighTime = DateTime.MinValue;
                priorCycleLowTime = DateTime.MinValue;
            }
        }

        protected override void OnBarUpdate()
        {
            int maxBars = useSecondComparison ? 3 : 2;
            if (CurrentBars[0] < 2 || CurrentBars[1] < 2)
                return;
            if (useSecondComparison && CurrentBars[2] < 2)
                return;
            
            // Get current 90-minute cycle (1-16 for the full day)
            int currentCycle = Get90MinuteCycle(Time[0]);
            
            if (currentCycle != lastCycle && lastCycle != -1)
            {
                // New cycle - reset for new cycle
                priorCycleHigh = GetPriorCycleHigh(0);
                priorCycleLow = GetPriorCycleLow(0);
                compPriorCycleHigh = GetPriorCycleHigh(1);
                compPriorCycleLow = GetPriorCycleLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorCycleHigh = GetPriorCycleHigh(2);
                    comp2PriorCycleLow = GetPriorCycleLow(2);
                }
                
                // Store the TIME of the prior cycle high/low
                priorCycleHighTime = GetPriorCycleHighTime();
                priorCycleLowTime = GetPriorCycleLowTime();
                
                primaryBrokeHigh = false;
                comparisonBrokeHigh = false;
                comparison2BrokeHigh = false;
                primaryBrokeLow = false;
                comparisonBrokeLow = false;
                comparison2BrokeLow = false;
            }
            
            if (lastCycle == -1)
            {
                // First run - initialize prior cycle values
                priorCycleHigh = GetPriorCycleHigh(0);
                priorCycleLow = GetPriorCycleLow(0);
                compPriorCycleHigh = GetPriorCycleHigh(1);
                compPriorCycleLow = GetPriorCycleLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorCycleHigh = GetPriorCycleHigh(2);
                    comp2PriorCycleLow = GetPriorCycleLow(2);
                }
                
                priorCycleHighTime = GetPriorCycleHighTime();
                priorCycleLowTime = GetPriorCycleLowTime();
            }
            
            lastCycle = currentCycle;
            
            // Check for breaks on current bar
            // Primary instrument (GC)
            if (BarsInProgress == 0)
            {
                if (High[0] > priorCycleHigh && !primaryBrokeHigh)
                {
                    primaryBrokeHigh = true;
                    CheckForSMTOnBreak(true, 0); // high break, primary
                }
                
                if (Low[0] < priorCycleLow && !primaryBrokeLow)
                {
                    primaryBrokeLow = true;
                    CheckForSMTOnBreak(false, 0); // low break, primary
                }
            }
            
            // Comparison instrument (SI)
            if (CurrentBars[1] >= 1)
            {
                if (Highs[1][0] > compPriorCycleHigh && !comparisonBrokeHigh)
                {
                    comparisonBrokeHigh = true;
                    CheckForSMTOnBreak(true, 1); // high break, comparison 1
                }
                
                if (Lows[1][0] < compPriorCycleLow && !comparisonBrokeLow)
                {
                    comparisonBrokeLow = true;
                    CheckForSMTOnBreak(false, 1); // low break, comparison 1
                }
            }
            
            // Second comparison instrument (PL) - if enabled
            if (useSecondComparison && CurrentBars[2] >= 1)
            {
                if (Highs[2][0] > comp2PriorCycleHigh && !comparison2BrokeHigh)
                {
                    comparison2BrokeHigh = true;
                    CheckForSMTOnBreak(true, 2); // high break, comparison 2
                }
                
                if (Lows[2][0] < comp2PriorCycleLow && !comparison2BrokeLow)
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
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " 90SSMT";
                    DrawSMTLine(true, priorCycleHighTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeHigh && (!useSecondComparison || !comparison2BrokeHigh))
                {
                    // Primary broke high, but none of the comparison instruments did
                    DrawSMTLine(true, priorCycleHighTime, false, ""); // false = no label
                }
            }
            else
            {
                // Bullish SMT - one broke low, check if primary hasn't
                if (whichInstrument > 0 && !primaryBrokeLow)
                {
                    // One of the comparison instruments broke low, primary didn't
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " 90SSMT";
                    DrawSMTLine(false, priorCycleLowTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeLow && (!useSecondComparison || !comparison2BrokeLow))
                {
                    // Primary broke low, but none of the comparison instruments did
                    DrawSMTLine(false, priorCycleLowTime, false, ""); // false = no label
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
            
            string tag = "SMT90mCycle" + (isBearish ? "Bear_" : "Bull_") + Time[0].ToString("yyyyMMddHHmmss");
            
            if (isBearish)
            {
                // Draw from prior cycle high to current candle high
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
                // Draw from prior cycle low to current candle low
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
        
        private int Get90MinuteCycle(DateTime time)
        {
            // Each day has 16 x 90-minute cycles (24 hours / 1.5 hours = 16)
            // Asian Session (18:00-23:59): Cycles 1-4
            //   Q1: 18:00-19:29 = Cycle 1
            //   Q2: 19:30-20:59 = Cycle 2
            //   Q3: 21:00-22:29 = Cycle 3
            //   Q4: 22:30-23:59 = Cycle 4
            // London Session (00:00-05:59): Cycles 5-8
            //   Q1: 00:00-01:29 = Cycle 5
            //   Q2: 01:30-02:59 = Cycle 6
            //   Q3: 03:00-04:29 = Cycle 7
            //   Q4: 04:30-05:59 = Cycle 8
            // NY Session (06:00-11:59): Cycles 9-12
            //   Q1: 06:00-07:29 = Cycle 9
            //   Q2: 07:30-08:59 = Cycle 10
            //   Q3: 09:00-10:29 = Cycle 11
            //   Q4: 10:30-11:59 = Cycle 12
            // PM Session (12:00-16:59): Cycles 13-16
            //   Q1: 12:00-13:29 = Cycle 13
            //   Q2: 13:30-14:59 = Cycle 14
            //   Q3: 15:00-16:29 = Cycle 15
            //   Q4: 16:30-16:59 = Cycle 16
            
            TimeSpan t = time.TimeOfDay;
            int hour = t.Hours;
            int minute = t.Minutes;
            
            // Asian Session (18:00-23:59)
            if (hour >= 18 && hour <= 23)
            {
                if (hour == 18 || (hour == 19 && minute < 30)) return 1;
                if ((hour == 19 && minute >= 30) || (hour == 20 && minute < 60)) return 2;
                if (hour == 21 || (hour == 22 && minute < 30)) return 3;
                if ((hour == 22 && minute >= 30) || hour == 23) return 4;
            }
            // London Session (00:00-05:59)
            else if (hour >= 0 && hour <= 5)
            {
                if (hour == 0 || (hour == 1 && minute < 30)) return 5;
                if ((hour == 1 && minute >= 30) || (hour == 2 && minute < 60)) return 6;
                if (hour == 3 || (hour == 4 && minute < 30)) return 7;
                if ((hour == 4 && minute >= 30) || hour == 5) return 8;
            }
            // NY Session (06:00-11:59)
            else if (hour >= 6 && hour <= 11)
            {
                if (hour == 6 || (hour == 7 && minute < 30)) return 9;
                if ((hour == 7 && minute >= 30) || (hour == 8 && minute < 60)) return 10;
                if (hour == 9 || (hour == 10 && minute < 30)) return 11;
                if ((hour == 10 && minute >= 30) || hour == 11) return 12;
            }
            // PM Session (12:00-16:59)
            else if (hour >= 12 && hour <= 16)
            {
                if (hour == 12 || (hour == 13 && minute < 30)) return 13;
                if ((hour == 13 && minute >= 30) || (hour == 14 && minute < 60)) return 14;
                if (hour == 15 || (hour == 16 && minute < 30)) return 15;
                if ((hour == 16 && minute >= 30)) return 16;
            }
            // Market close (17:00-17:59)
            else if (hour == 17)
            {
                return 0;
            }
            
            return 0;
        }
        
        private double GetPriorCycleHigh(int barsSeriesIndex)
        {
            double high = double.MinValue;
            int currentCycle = Get90MinuteCycle(Times[barsSeriesIndex][0]);
            int priorCycle = currentCycle - 1;
            if (priorCycle == 0) priorCycle = 16; // Wrap around
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 500); i++)
            {
                int barCycle = Get90MinuteCycle(Times[barsSeriesIndex][i]);
                
                if (barCycle == priorCycle)
                {
                    if (barsSeriesIndex == 0)
                        high = Math.Max(high, High[i]);
                    else
                        high = Math.Max(high, Highs[barsSeriesIndex][i]);
                }
                else if (barCycle != currentCycle && barCycle != priorCycle && barCycle != 0)
                {
                    // We've gone too far back
                    break;
                }
            }
            
            return high;
        }
        
        private double GetPriorCycleLow(int barsSeriesIndex)
        {
            double low = double.MaxValue;
            int currentCycle = Get90MinuteCycle(Times[barsSeriesIndex][0]);
            int priorCycle = currentCycle - 1;
            if (priorCycle == 0) priorCycle = 16; // Wrap around
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 500); i++)
            {
                int barCycle = Get90MinuteCycle(Times[barsSeriesIndex][i]);
                
                if (barCycle == priorCycle)
                {
                    if (barsSeriesIndex == 0)
                        low = Math.Min(low, Low[i]);
                    else
                        low = Math.Min(low, Lows[barsSeriesIndex][i]);
                }
                else if (barCycle != currentCycle && barCycle != priorCycle && barCycle != 0)
                {
                    // We've gone too far back
                    break;
                }
            }
            
            return low;
        }
        
        private DateTime GetPriorCycleHighTime()
        {
            double targetHigh = priorCycleHigh;
            if (targetHigh == double.MinValue)
                return DateTime.MinValue;
            
            int currentCycle = Get90MinuteCycle(Time[0]);
            int priorCycle = currentCycle - 1;
            if (priorCycle == 0) priorCycle = 16;
            
            // Search backwards for the bar that made the prior cycle high
            for (int i = 1; i < Math.Min(CurrentBar, 500); i++)
            {
                int barCycle = Get90MinuteCycle(Time[i]);
                
                if (barCycle == priorCycle && Math.Abs(High[i] - targetHigh) < 0.01)
                {
                    return Time[i];
                }
                else if (barCycle != currentCycle && barCycle != priorCycle && barCycle != 0)
                {
                    break;
                }
            }
            
            return DateTime.MinValue;
        }
        
        private DateTime GetPriorCycleLowTime()
        {
            double targetLow = priorCycleLow;
            if (targetLow == double.MaxValue)
                return DateTime.MinValue;
            
            int currentCycle = Get90MinuteCycle(Time[0]);
            int priorCycle = currentCycle - 1;
            if (priorCycle == 0) priorCycle = 16;
            
            // Search backwards for the bar that made the prior cycle low
            for (int i = 1; i < Math.Min(CurrentBar, 500); i++)
            {
                int barCycle = Get90MinuteCycle(Time[i]);
                
                if (barCycle == priorCycle && Math.Abs(Low[i] - targetLow) < 0.01)
                {
                    return Time[i];
                }
                else if (barCycle != currentCycle && barCycle != priorCycle && barCycle != 0)
                {
                    break;
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
		private SMT90mCycle[] cacheSMT90mCycle;
		public SMT90mCycle SMT90mCycle(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return SMT90mCycle(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public SMT90mCycle SMT90mCycle(ISeries<double> input, string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			if (cacheSMT90mCycle != null)
				for (int idx = 0; idx < cacheSMT90mCycle.Length; idx++)
					if (cacheSMT90mCycle[idx] != null && cacheSMT90mCycle[idx].ComparisonTicker == comparisonTicker && cacheSMT90mCycle[idx].ComparisonTicker2 == comparisonTicker2 && cacheSMT90mCycle[idx].BearishSMTColor == bearishSMTColor && cacheSMT90mCycle[idx].BullishSMTColor == bullishSMTColor && cacheSMT90mCycle[idx].LineWidth == lineWidth && cacheSMT90mCycle[idx].EqualsInput(input))
						return cacheSMT90mCycle[idx];
			return CacheIndicator<SMT90mCycle>(new SMT90mCycle(){ ComparisonTicker = comparisonTicker, ComparisonTicker2 = comparisonTicker2, BearishSMTColor = bearishSMTColor, BullishSMTColor = bullishSMTColor, LineWidth = lineWidth }, input, ref cacheSMT90mCycle);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SMT90mCycle SMT90mCycle(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMT90mCycle(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMT90mCycle SMT90mCycle(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMT90mCycle(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SMT90mCycle SMT90mCycle(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMT90mCycle(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMT90mCycle SMT90mCycle(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMT90mCycle(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

#endregion
