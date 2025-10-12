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
    public class SMTSessionSequential : Indicator
    {
        private Series<double> comparisonSeries;
        private double priorSessionHigh;
        private double priorSessionLow;
        private double compPriorSessionHigh;
        private double compPriorSessionLow;
        private double comp2PriorSessionHigh;
        private double comp2PriorSessionLow;
        private DateTime priorSessionHighTime;
        private DateTime priorSessionLowTime;
        private int lastSession;
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
                Description = @"Detects SMT divergences between sessions (Q1->Q2->Q3->Q4)";
                Name = "SSMT Daily Cycle";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
                
                ComparisonTicker = "SI 12-25";
                ComparisonTicker2 = "PL 01-26";
                BearishSMTColor = Brushes.Violet;
                BullishSMTColor = Brushes.Violet;
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
                priorSessionHigh = double.MinValue;
                priorSessionLow = double.MaxValue;
                compPriorSessionHigh = double.MinValue;
                compPriorSessionLow = double.MaxValue;
                comp2PriorSessionHigh = double.MinValue;
                comp2PriorSessionLow = double.MaxValue;
                lastSession = -1;
                priorSessionHighTime = DateTime.MinValue;
                priorSessionLowTime = DateTime.MinValue;
            }
        }

        protected override void OnBarUpdate()
        {
            int maxBars = useSecondComparison ? 3 : 2;
            if (CurrentBars[0] < 2 || CurrentBars[1] < 2)
                return;
            if (useSecondComparison && CurrentBars[2] < 2)
                return;
            
            // Get current session (1=Q1/Asian, 2=Q2/London, 3=Q3/NY, 4=Q4/PM)
            int currentSession = GetSessionQuarter(Time[0]);
            
            if (currentSession != lastSession && lastSession != -1)
            {
                // New session - reset for new session
                priorSessionHigh = GetPriorSessionHigh(0);
                priorSessionLow = GetPriorSessionLow(0);
                compPriorSessionHigh = GetPriorSessionHigh(1);
                compPriorSessionLow = GetPriorSessionLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorSessionHigh = GetPriorSessionHigh(2);
                    comp2PriorSessionLow = GetPriorSessionLow(2);
                }
                
                // Store the TIME of the prior session high/low
                priorSessionHighTime = GetPriorSessionHighTime();
                priorSessionLowTime = GetPriorSessionLowTime();
                
                primaryBrokeHigh = false;
                comparisonBrokeHigh = false;
                comparison2BrokeHigh = false;
                primaryBrokeLow = false;
                comparisonBrokeLow = false;
                comparison2BrokeLow = false;
            }
            
            if (lastSession == -1)
            {
                // First run - initialize prior session values
                priorSessionHigh = GetPriorSessionHigh(0);
                priorSessionLow = GetPriorSessionLow(0);
                compPriorSessionHigh = GetPriorSessionHigh(1);
                compPriorSessionLow = GetPriorSessionLow(1);
                
                if (useSecondComparison)
                {
                    comp2PriorSessionHigh = GetPriorSessionHigh(2);
                    comp2PriorSessionLow = GetPriorSessionLow(2);
                }
                
                priorSessionHighTime = GetPriorSessionHighTime();
                priorSessionLowTime = GetPriorSessionLowTime();
            }
            
            lastSession = currentSession;
            
            // Check for breaks on current bar
            // Primary instrument (GC)
            if (BarsInProgress == 0)
            {
                if (High[0] > priorSessionHigh && !primaryBrokeHigh)
                {
                    primaryBrokeHigh = true;
                    CheckForSMTOnBreak(true, 0); // high break, primary
                }
                
                if (Low[0] < priorSessionLow && !primaryBrokeLow)
                {
                    primaryBrokeLow = true;
                    CheckForSMTOnBreak(false, 0); // low break, primary
                }
            }
            
            // Comparison instrument (SI)
            if (CurrentBars[1] >= 1)
            {
                if (Highs[1][0] > compPriorSessionHigh && !comparisonBrokeHigh)
                {
                    comparisonBrokeHigh = true;
                    CheckForSMTOnBreak(true, 1); // high break, comparison 1
                }
                
                if (Lows[1][0] < compPriorSessionLow && !comparisonBrokeLow)
                {
                    comparisonBrokeLow = true;
                    CheckForSMTOnBreak(false, 1); // low break, comparison 1
                }
            }
            
            // Second comparison instrument (PL) - if enabled
            if (useSecondComparison && CurrentBars[2] >= 1)
            {
                if (Highs[2][0] > comp2PriorSessionHigh && !comparison2BrokeHigh)
                {
                    comparison2BrokeHigh = true;
                    CheckForSMTOnBreak(true, 2); // high break, comparison 2
                }
                
                if (Lows[2][0] < comp2PriorSessionLow && !comparison2BrokeLow)
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
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " DCSSMT";
                    DrawSMTLine(true, priorSessionHighTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeHigh && (!useSecondComparison || !comparison2BrokeHigh))
                {
                    // Primary broke high, but none of the comparison instruments did
                    DrawSMTLine(true, priorSessionHighTime, false, ""); // false = no label
                }
            }
            else
            {
                // Bullish SMT - one broke low, check if primary hasn't
                if (whichInstrument > 0 && !primaryBrokeLow)
                {
                    // One of the comparison instruments broke low, primary didn't
                    string label = (whichInstrument == 1 ? ComparisonTicker : ComparisonTicker2) + " DCSSMT";
                    DrawSMTLine(false, priorSessionLowTime, true, label); // true = show label
                }
                else if (whichInstrument == 0 && !comparisonBrokeLow && (!useSecondComparison || !comparison2BrokeLow))
                {
                    // Primary broke low, but none of the comparison instruments did
                    DrawSMTLine(false, priorSessionLowTime, false, ""); // false = no label
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
            
            string tag = "SMTSession_" + (isBearish ? "Bear_" : "Bull_") + Time[0].ToString("yyyyMMddHHmmss");
            
            if (isBearish)
            {
                // Draw from prior session high to current candle high
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
                    string symbol = labelTicker.Split(' ')[0] + "-" + labelTicker.Split(' ')[2];
                    
                    Draw.Text(this, tag + "_Label", symbol, midBar, midPrice + (20 * TickSize), BearishSMTColor);
                }
            }
            else
            {
                // Draw from prior session low to current candle low
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
        
        private int GetSessionQuarter(DateTime time)
        {
            // Q1 (Asian): 18:00-23:59 = 1
            // Q2 (London): 00:00-05:59 = 2
            // Q3 (NY): 06:00-11:59 = 3
            // Q4 (PM): 12:00-16:59 = 4
            
            TimeSpan t = time.TimeOfDay;
            
            if (t >= new TimeSpan(18, 0, 0) && t <= new TimeSpan(23, 59, 59))
                return 1; // Q1 Asian
            else if (t >= new TimeSpan(0, 0, 0) && t <= new TimeSpan(5, 59, 59))
                return 2; // Q2 London
            else if (t >= new TimeSpan(6, 0, 0) && t <= new TimeSpan(11, 59, 59))
                return 3; // Q3 NY
            else if (t >= new TimeSpan(12, 0, 0) && t <= new TimeSpan(16, 59, 59))
                return 4; // Q4 PM
            else if (t >= new TimeSpan(17, 0, 0) && t < new TimeSpan(18, 0, 0))
                return 0; // Market closed hour
            else
                return 0; // Shouldn't happen
        }
        
        private double GetPriorSessionHigh(int barsSeriesIndex)
        {
            double high = double.MinValue;
            int currentSession = GetSessionQuarter(Times[barsSeriesIndex][0]);
            int priorSession = currentSession - 1;
            if (priorSession == 0) priorSession = 4; // Wrap around Q1 -> Q4
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 500); i++)
            {
                int barSession = GetSessionQuarter(Times[barsSeriesIndex][i]);
                
                if (barSession == priorSession)
                {
                    if (barsSeriesIndex == 0)
                        high = Math.Max(high, High[i]);
                    else
                        high = Math.Max(high, Highs[barsSeriesIndex][i]);
                }
                else if (barSession != currentSession && barSession != priorSession)
                {
                    // We've gone too far back
                    break;
                }
            }
            
            return high;
        }
        
        private double GetPriorSessionLow(int barsSeriesIndex)
        {
            double low = double.MaxValue;
            int currentSession = GetSessionQuarter(Times[barsSeriesIndex][0]);
            int priorSession = currentSession - 1;
            if (priorSession == 0) priorSession = 4; // Wrap around Q1 -> Q4
            
            for (int i = 1; i < Math.Min(CurrentBars[barsSeriesIndex], 500); i++)
            {
                int barSession = GetSessionQuarter(Times[barsSeriesIndex][i]);
                
                if (barSession == priorSession)
                {
                    if (barsSeriesIndex == 0)
                        low = Math.Min(low, Low[i]);
                    else
                        low = Math.Min(low, Lows[barsSeriesIndex][i]);
                }
                else if (barSession != currentSession && barSession != priorSession)
                {
                    // We've gone too far back
                    break;
                }
            }
            
            return low;
        }
        
        private DateTime GetPriorSessionHighTime()
        {
            double targetHigh = priorSessionHigh;
            if (targetHigh == double.MinValue)
                return DateTime.MinValue;
            
            int currentSession = GetSessionQuarter(Time[0]);
            int priorSession = currentSession - 1;
            if (priorSession == 0) priorSession = 4;
            
            // Search backwards for the bar that made the prior session high
            for (int i = 1; i < Math.Min(CurrentBar, 500); i++)
            {
                int barSession = GetSessionQuarter(Time[i]);
                
                if (barSession == priorSession && Math.Abs(High[i] - targetHigh) < 0.01)
                {
                    return Time[i];
                }
                else if (barSession != currentSession && barSession != priorSession)
                {
                    break;
                }
            }
            
            return DateTime.MinValue;
        }
        
        private DateTime GetPriorSessionLowTime()
        {
            double targetLow = priorSessionLow;
            if (targetLow == double.MaxValue)
                return DateTime.MinValue;
            
            int currentSession = GetSessionQuarter(Time[0]);
            int priorSession = currentSession - 1;
            if (priorSession == 0) priorSession = 4;
            
            // Search backwards for the bar that made the prior session low
            for (int i = 1; i < Math.Min(CurrentBar, 500); i++)
            {
                int barSession = GetSessionQuarter(Time[i]);
                
                if (barSession == priorSession && Math.Abs(Low[i] - targetLow) < 0.01)
                {
                    return Time[i];
                }
                else if (barSession != currentSession && barSession != priorSession)
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
		private SMTSessionSequential[] cacheSMTSessionSequential;
		public SMTSessionSequential SMTSessionSequential(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return SMTSessionSequential(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public SMTSessionSequential SMTSessionSequential(ISeries<double> input, string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			if (cacheSMTSessionSequential != null)
				for (int idx = 0; idx < cacheSMTSessionSequential.Length; idx++)
					if (cacheSMTSessionSequential[idx] != null && cacheSMTSessionSequential[idx].ComparisonTicker == comparisonTicker && cacheSMTSessionSequential[idx].ComparisonTicker2 == comparisonTicker2 && cacheSMTSessionSequential[idx].BearishSMTColor == bearishSMTColor && cacheSMTSessionSequential[idx].BullishSMTColor == bullishSMTColor && cacheSMTSessionSequential[idx].LineWidth == lineWidth && cacheSMTSessionSequential[idx].EqualsInput(input))
						return cacheSMTSessionSequential[idx];
			return CacheIndicator<SMTSessionSequential>(new SMTSessionSequential(){ ComparisonTicker = comparisonTicker, ComparisonTicker2 = comparisonTicker2, BearishSMTColor = bearishSMTColor, BullishSMTColor = bullishSMTColor, LineWidth = lineWidth }, input, ref cacheSMTSessionSequential);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SMTSessionSequential SMTSessionSequential(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTSessionSequential(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMTSessionSequential SMTSessionSequential(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTSessionSequential(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SMTSessionSequential SMTSessionSequential(string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTSessionSequential(Input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}

		public Indicators.SMTSessionSequential SMTSessionSequential(ISeries<double> input , string comparisonTicker, string comparisonTicker2, Brush bearishSMTColor, Brush bullishSMTColor, int lineWidth)
		{
			return indicator.SMTSessionSequential(input, comparisonTicker, comparisonTicker2, bearishSMTColor, bullishSMTColor, lineWidth);
		}
	}
}

#endregion
