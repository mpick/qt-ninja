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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TrendLineTrader : Strategy
    {
	    private Swing swing;
	    private List<SwingData> swings = new List<SwingData>(); // Changed from swingHighs to generic swings
	    private List<TrendlineData> activeTrendlines = new List<TrendlineData>();
	    private const double PriceSanityMultiplier = 1.5;
	    private const int BreakoutConfirmBars = 3;
	    private string logPrefix = "";
	    private double lastSwingValue = double.NaN;
	    private int lastSwingBar = -1;

        // Struct for swing data
        private struct SwingData
        {
            public int BarIndex;
            public double Value;
        }

        // Struct for trendline data
        private struct TrendlineData
        {
            public int StartBar; // Second-last swing bar
            public double StartY;
            public int EndBar; // Last swing bar
            public double EndY;
            public double Slope;
            public string Tag;
            public int BarsBroken; // Track confirmations above
        }

		protected override void OnStateChange()
		{
		    if (State == State.SetDefaults)
		    {
		        Description = @"Multi-trendline breakout pullback with dual profit targets";
		        Name = "TrendlineBreakoutPullback";
		        Calculate = Calculate.OnBarClose;
		        EntriesPerDirection = 2; // Allow 2 entries
		        EntryHandling = EntryHandling.AllEntries;
		        IsExitOnSessionCloseStrategy = true;
		        ExitOnSessionCloseSeconds = 30;
		        IsFillLimitOnTouch = false;
		        MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
		        OrderFillResolution = OrderFillResolution.Standard;
		        Slippage = 0;
				StartBehavior = StartBehavior.ImmediatelySubmit;
		        TimeInForce = TimeInForce.Gtc;
		        TraceOrders = false;
		        RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
		        StopTargetHandling = StopTargetHandling.PerEntryExecution;
		        BarsRequiredToTrade = 50;
		        SwingStrength = 10;
		        MaxTrendlines = 5;
		        MinSlopeThreshold = -0.1;
		        MinBarsBetweenSwings = 3;
		        Contracts1to1RR = 1;
		        ContractsHalfRR = 1;
		    }
		    else if (State == State.DataLoaded)
		    {
		        swing = Swing(SwingStrength);
		        logPrefix = $"[{Bars.Instrument.FullName}] ";
		        ClearOutputWindow();
		        Print($"{logPrefix}Strategy loaded: Max trendlines={MaxTrendlines}");
		    }
		}

		protected override void OnBarUpdate()
		{
		    // Skip all historical bar processing
		    if (State == State.Historical)
		        return;
			
		    if (CurrentBar < BarsRequiredToTrade)
		        return;
			
			// FORCE position sync - check actual account position
		    if (Position.MarketPosition != MarketPosition.Flat)
		    {
		        // Log what the strategy THINKS vs reality
		        Print($"{logPrefix}Strategy thinks Position={Position.MarketPosition}, Quantity={Position.Quantity}");
		        
		        // If we're supposedly in a position but have no orders, reset it
		        if (Orders.Count == 0)
		        {
		            Print($"{logPrefix}WARNING: Strategy thinks we have position but no orders exist!");
		        }
		    }
		
		    // Handle swing detection based on direction
		    if (Direction == TradeDirection.Bullish)
		    {
		        DetectBullishSwings();
		    }
		    else
		    {
		        DetectBearishSwings();
		    }
		
		    // Check trendlines for breakout
		    CheckTrendlinesForBreakout();
		}
		
		private void DetectBullishSwings()
		{
		    // Detect swing HIGHS for downtrend breakouts
		    if (!double.IsNaN(swing.SwingHigh[0]))
		    {
		        double newSwing = swing.SwingHigh[0];
		        int swingBar = CurrentBar - swing.SwingHighBar(0, 1, CurrentBar);
		        
		        // Only add if this is a NEW swing
		        if (newSwing != lastSwingValue || swingBar != lastSwingBar)
		        {
		            lastSwingValue = newSwing;
		            lastSwingBar = swingBar;
		            
		            // Sanity check
		            double currentClose = Close[0];
		            if (newSwing > currentClose * PriceSanityMultiplier || newSwing < currentClose / PriceSanityMultiplier)
		            {
		                Print($"{logPrefix}Ignored bogus swing {newSwing:F2} at bar {swingBar}");
		                return;
		            }
		
		            swings.Add(new SwingData { BarIndex = swingBar, Value = newSwing });
		            if (swings.Count > MaxTrendlines * 2)
		                swings.RemoveAt(0);
		
		            Print($"{logPrefix}NEW swing HIGH #{swings.Count} at bar {swingBar}: {newSwing:F2}");
		            UpdateTrendlines();
		        }
		    }
		}
		
		private void DetectBearishSwings()
		{
		    // Detect swing LOWS for uptrend breakdowns
		    if (!double.IsNaN(swing.SwingLow[0]))
		    {
		        double newSwing = swing.SwingLow[0];
		        int swingBar = CurrentBar - swing.SwingLowBar(0, 1, CurrentBar);
		        
		        // Only add if this is a NEW swing
		        if (newSwing != lastSwingValue || swingBar != lastSwingBar)
		        {
		            lastSwingValue = newSwing;
		            lastSwingBar = swingBar;
		            
		            // Sanity check
		            double currentClose = Close[0];
		            if (newSwing > currentClose * PriceSanityMultiplier || newSwing < currentClose / PriceSanityMultiplier)
		            {
		                Print($"{logPrefix}Ignored bogus swing {newSwing:F2} at bar {swingBar}");
		                return;
		            }
		
		            swings.Add(new SwingData { BarIndex = swingBar, Value = newSwing });
		            if (swings.Count > MaxTrendlines * 2)
		                swings.RemoveAt(0);
		
		            Print($"{logPrefix}NEW swing LOW #{swings.Count} at bar {swingBar}: {newSwing:F2}");
		            UpdateTrendlines();
		        }
		    }
		}

		private void UpdateTrendlines()
		{
		    // Clear old drawings
		    foreach (var tl in activeTrendlines.ToList())
		        RemoveDrawObject(tl.Tag);
		
		    activeTrendlines.Clear();
		
		    // Build from consecutive swing pairs
		    for (int i = 0; i < swings.Count - 1; i++)
		    {
		        var secondLast = swings[i];
		        var last = swings[i + 1];
		
		        int deltaBars = last.BarIndex - secondLast.BarIndex;
		        if (deltaBars < MinBarsBetweenSwings)
		        {
		            Print($"{logPrefix}Skipped pair: Delta bars {deltaBars} < {MinBarsBetweenSwings}");
		            continue;
		        }
		
		        double slope = (last.Value - secondLast.Value) / deltaBars;
		        
		        // Filter by slope based on direction
		        bool validSlope = false;
		        if (Direction == TradeDirection.Bullish)
		        {
		            // For bullish: need downward slope (negative)
		            validSlope = slope <= MinSlopeThreshold;
		            if (!validSlope)
		                Print($"{logPrefix}Skipped pair: Slope {slope:F4} > {MinSlopeThreshold} (not downtrend)");
		        }
		        else
		        {
		            // For bearish: need upward slope (positive) - flip the threshold
		            validSlope = slope >= Math.Abs(MinSlopeThreshold);
		            if (!validSlope)
		                Print($"{logPrefix}Skipped pair: Slope {slope:F4} < {Math.Abs(MinSlopeThreshold)} (not uptrend)");
		        }
		        
		        if (!validSlope)
		            continue;
		
		        // Create trendline
		        var tlData = new TrendlineData
		        {
		            StartBar = secondLast.BarIndex,
		            StartY = secondLast.Value,
		            EndBar = last.BarIndex,
		            EndY = last.Value,
		            Slope = slope,
		            Tag = "TL_" + last.BarIndex,
		            BarsBroken = 0
		        };
		
		        activeTrendlines.Add(tlData);
		
		        // Draw it
		        int startBarsAgo = CurrentBar - secondLast.BarIndex;
		        double currentY = last.Value + (slope * (CurrentBar - last.BarIndex));
		        
		        Brush lineColor = Direction == TradeDirection.Bullish ? Brushes.Blue : Brushes.Red;
		        Draw.Line(this, tlData.Tag, false, startBarsAgo, secondLast.Value, 0, currentY,
		            lineColor, DashStyleHelper.Dash, 2);
		
		        Print($"{logPrefix}Drew trendline #{activeTrendlines.Count}: From {secondLast.Value:F2} (bar {secondLast.BarIndex}) to {last.Value:F2} (bar {last.BarIndex}), Slope={slope:F4}, Current Y={currentY:F2}");
		    
		        // TRIGGER ALERT FOR NEW TRENDLINE
				string alertMessage = Direction == TradeDirection.Bullish 
				    ? $"📉 New DOWNTREND: {Instrument.FullName} | High 1: {secondLast.Value:F2} → High 2: {last.Value:F2} | Slope: {slope:F4}" 
				    : $"📈 New UPTREND: {Instrument.FullName} | Low 1: {secondLast.Value:F2} → Low 2: {last.Value:F2} | Slope: {slope:F4}";
				
				Alert("NewTrendline_" + tlData.Tag, Priority.Medium, alertMessage, 
				    NinjaTrader.Core.Globals.InstallDir + @"\sounds\ding.wav", 10, Brushes.Yellow, Brushes.Black);
		
						
			}
		
		    // Limit to max
		    while (activeTrendlines.Count > MaxTrendlines)
		    {
		        RemoveDrawObject(activeTrendlines[0].Tag);
		        activeTrendlines.RemoveAt(0);
		    }
		
		    if (activeTrendlines.Count == 0)
		        Print($"{logPrefix}No valid trendlines drawn this bar (check filters)");
		}

		private void CheckTrendlinesForBreakout()
		{
		    for (int i = 0; i < activeTrendlines.Count; i++)
		    {
		        var tl = activeTrendlines[i];
		        double barsFromEnd = CurrentBar - tl.EndBar;
		        double prevBarsFromEnd = barsFromEnd - 1;
		        double currentTLValue = tl.EndY + (tl.Slope * barsFromEnd);
		        double prevTLValue = tl.EndY + (tl.Slope * prevBarsFromEnd);
		
		        bool breakoutOccurred = false;
		        bool wasOnCorrectSide = false;
		        
		        if (Direction == TradeDirection.Bullish)
		        {
		            // Bullish: breakout when price crosses ABOVE downtrend line
		            breakoutOccurred = Close[0] > currentTLValue;
		            wasOnCorrectSide = Close[1] <= prevTLValue;
		        }
		        else
		        {
		            // Bearish: breakdown when price crosses BELOW uptrend line
		            breakoutOccurred = Close[0] < currentTLValue;
		            wasOnCorrectSide = Close[1] >= prevTLValue;
		        }
		
		        Print($"{logPrefix}DEBUG Breakout Check on {tl.Tag}: wasOnCorrectSide={wasOnCorrectSide}, breakoutOccurred={breakoutOccurred}, BarsBroken={tl.BarsBroken}, Position={Position.MarketPosition}");
		        
		        // Check for FRESH breakout - only when BarsBroken is still 0
		        if (wasOnCorrectSide && breakoutOccurred && tl.BarsBroken == 0 && Position.MarketPosition == MarketPosition.Flat)
		        {
		            Print($"{logPrefix}BREAKOUT on TL {tl.Tag}: Prior={Close[1]:F2}, Current={Close[0]:F2}, TL={currentTLValue:F2}");
		            
		            // Calculate stop loss and targets based on direction
		            double stopLossPrice, entryPrice, riskPerContract, targetHalfRR, target1to1;
		            
		            if (Direction == TradeDirection.Bullish)
		            {
		                // LONG setup
		                stopLossPrice = GetLowestLowSinceBar(tl.EndBar);
		                entryPrice = Close[0];
		                riskPerContract = entryPrice - stopLossPrice;
		                
		                if (stopLossPrice >= entryPrice)
		                {
		                    Print($"{logPrefix}ERROR: Stop loss {stopLossPrice:F2} is not below entry {entryPrice:F2}. Skipping trade.");
		                    continue;
		                }
		                
		                targetHalfRR = entryPrice + (riskPerContract * 0.5);
		                target1to1 = entryPrice + riskPerContract;
		            }
		            else
		            {
		                // SHORT setup
		                stopLossPrice = GetHighestHighSinceBar(tl.EndBar);
		                entryPrice = Close[0];
		                riskPerContract = stopLossPrice - entryPrice;
		                
		                if (stopLossPrice <= entryPrice)
		                {
		                    Print($"{logPrefix}ERROR: Stop loss {stopLossPrice:F2} is not above entry {entryPrice:F2}. Skipping trade.");
		                    continue;
		                }
		                
		                targetHalfRR = entryPrice - (riskPerContract * 0.5);
		                target1to1 = entryPrice - riskPerContract;
		            }
		            
		            Print($"{logPrefix}Entry Setup: Entry={entryPrice:F2}, Stop={stopLossPrice:F2}, Risk={riskPerContract:F2}, Target0.5:1={targetHalfRR:F2}, Target1:1={target1to1:F2}");
		            
		            // Place entries based on direction
		            string entry1Signal = $"TrendBreak_Half_{tl.Tag}";
		            string entry2Signal = $"TrendBreak_1to1_{tl.Tag}";
		            
		            if (Direction == TradeDirection.Bullish)
		            {
		                // LONG entries
		                EnterLong(ContractsHalfRR, entry1Signal);
		                SetStopLoss(entry1Signal, CalculationMode.Price, stopLossPrice, false);
		                SetProfitTarget(entry1Signal, CalculationMode.Price, targetHalfRR);
		                
		                EnterLong(Contracts1to1RR, entry2Signal);
		                SetStopLoss(entry2Signal, CalculationMode.Price, stopLossPrice, false);
		                SetProfitTarget(entry2Signal, CalculationMode.Price, target1to1);
		                
		                Draw.TriangleUp(this, $"Breakout_{CurrentBar}", false, 0, Low[0] - (2 * TickSize), Brushes.Lime);
		            }
		            else
		            {
		                // SHORT entries
		                EnterShort(ContractsHalfRR, entry1Signal);
		                SetStopLoss(entry1Signal, CalculationMode.Price, stopLossPrice, false);
		                SetProfitTarget(entry1Signal, CalculationMode.Price, targetHalfRR);
		                
		                EnterShort(Contracts1to1RR, entry2Signal);
		                SetStopLoss(entry2Signal, CalculationMode.Price, stopLossPrice, false);
		                SetProfitTarget(entry2Signal, CalculationMode.Price, target1to1);
		                
		                Draw.TriangleDown(this, $"Breakout_{CurrentBar}", false, 0, High[0] + (2 * TickSize), Brushes.Red);
		            }
		            
		            // Draw stop and target levels
		            Draw.Line(this, $"Stop_{CurrentBar}", false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Red, DashStyleHelper.Solid, 2);
		            Draw.Line(this, $"Target_Half_{CurrentBar}", false, 0, targetHalfRR, 10, targetHalfRR, Brushes.Pink, DashStyleHelper.Dash, 2);
		            Draw.Line(this, $"Target1_{CurrentBar}", false, 0, target1to1, 10, target1to1, Brushes.LimeGreen, DashStyleHelper.Dash, 2);
		        }
		
		        // Increment broken bars counter
		        if (breakoutOccurred)
		        {
		            tl.BarsBroken++;
		            activeTrendlines[i] = tl;
		        }
		        else
		        {
		            tl.BarsBroken = 0;
		            activeTrendlines[i] = tl;
		        }
		
		        // Expire if broken for confirm bars
		        if (tl.BarsBroken >= BreakoutConfirmBars)
		        {
		            Print($"{logPrefix}Expiring broken TL {tl.Tag} after {tl.BarsBroken} bars");
		            RemoveDrawObject(tl.Tag);
		            activeTrendlines.RemoveAt(i);
		            i--;
		        }
		        else
		        {
		            Print($"{logPrefix}TL {tl.Tag} status: Current={Close[0]:F2} vs {currentTLValue:F2} (broken bars: {tl.BarsBroken})");
		        }
		    }
		}
		
		// NEW Helper method: Find highest high from a specific bar until NOW (for short stops)
		private double GetHighestHighSinceBar(int sinceBar)
		{
		    double highestHigh = double.MinValue;
		    int barsAgo = CurrentBar - sinceBar;
		    
		    for (int i = 0; i <= barsAgo; i++)
		    {
		        if (i < CurrentBar && High[i] > highestHigh)
		        {
		            highestHigh = High[i];
		        }
		    }
		    
		    Print($"{logPrefix}Highest high since bar {sinceBar} ({barsAgo} bars ago): {highestHigh:F2}");
		    return highestHigh;
		}		
		
		// NEW Helper method: Find lowest low from a specific bar until NOW
		private double GetLowestLowSinceBar(int sinceBar)
		{
		    double lowestLow = double.MaxValue;
		    int barsAgo = CurrentBar - sinceBar;
		    
		    // Search from the swing high bar until current bar
		    for (int i = 0; i <= barsAgo; i++)
		    {
		        if (i < CurrentBar && Low[i] < lowestLow)
		        {
		            lowestLow = Low[i];
		        }
		    }
		    
		    Print($"{logPrefix}Lowest low since bar {sinceBar} ({barsAgo} bars ago): {lowestLow:F2}");
		    return lowestLow;
		}
		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="SwingStrength", Description="Swing strength", Order=1, GroupName="Parameters")]
		public int SwingStrength { get; set; } = 10;
		
		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name="MaxTrendlines", Description="Max historical trendlines to track", Order=2, GroupName="Parameters")]
		public int MaxTrendlines { get; set; } = 5;
		
		[NinjaScriptProperty]
		[Display(Name="MinSlopeThreshold", Description="Min slope for valid downtrend (negative)", Order=3, GroupName="Parameters")]
		public double MinSlopeThreshold { get; set; } = -0.1;
		
		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name="MinBarsBetweenSwings", Description="Min bars between swings for valid pair", Order=4, GroupName="Parameters")]
		public int MinBarsBetweenSwings { get; set; } = 3;
		
		// NEW: Trade Direction
		[NinjaScriptProperty]
		[Display(Name="Trade Direction", Description="Choose whether to trade bullish breakouts (long) or bearish breakdowns (short)", Order=5, GroupName="Parameters")]
		public TradeDirection Direction { get; set; } = TradeDirection.Bullish;
		
		// CONTRACT QUANTITIES
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Contracts 1:1 RR", Description="Number of contracts for 1:1 risk/reward target", Order=6, GroupName="Position Sizing")]
		public int Contracts1to1RR { get; set; } = 1;
		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Contracts 0.5:1 RR", Description="Number of contracts for 0.5:1 risk/reward target (early partial profit)", Order=7, GroupName="Position Sizing")]
		public int ContractsHalfRR { get; set; } = 1;
		#endregion
		
		// Enum for trade direction
		public enum TradeDirection
		{
		    Bullish,
		    Bearish
		}
    }
}
