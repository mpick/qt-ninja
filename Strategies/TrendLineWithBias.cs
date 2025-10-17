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
    public class TrendLineTraderWithBias_v2 : Strategy
    {
        private Swing swing;
        private List<SwingData> swings = new List<SwingData>();
        private List<TrendlineData> activeTrendlines = new List<TrendlineData>();
        private const double PriceSanityMultiplier = 1.5;
        private const int BreakoutConfirmBars = 3;
        private string logPrefix = "";
        private double lastSwingValue = double.NaN;
        private int lastSwingBar = -1;
        
        // DFR tracking variables
        private bool inDFRPeriod = false;
        private int currentDFRStartBar = -1;
        private double currentDFRHigh = double.MinValue;
        private double currentDFRLow = double.MaxValue;
        private double activeDFRHigh = double.MinValue;
        private double activeDFRLow = double.MaxValue;
        private DateTime currentDayDate = DateTime.MinValue;
        private bool dfrEstablished = false;
        
        // Bias tracking
        private TradeDirection currentBias = TradeDirection.Bullish;
        private bool canTrade = false;

        private struct SwingData
        {
            public int BarIndex;
            public double Value;
        }

        private struct TrendlineData
        {
            public int StartBar;
            public double StartY;
            public int EndBar;
            public double EndY;
            public double Slope;
            public string Tag;
            public int BarsBroken;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Trendline breakout strategy with automatic bias detection based on DFR levels";
                Name = "TrendLineTraderWithBias_v2";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 2;
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
                MinSlopeThreshold = 0.1;
                MinBarsBetweenSwings = 3;
                Contracts1to1RR = 1;
                ContractsHalfRR = 1;
                BarsToCheckForBias = 10;
                DrawDFRBox = true;
            }
            else if (State == State.DataLoaded)
            {
                swing = Swing(SwingStrength);
                logPrefix = $"[{Bars.Instrument.FullName}] ";
                ClearOutputWindow();
                Print($"{logPrefix}Strategy loaded with auto-bias detection");
                Print($"{logPrefix}Trading hours: 00:00 - 16:59 EST only");
            }
            else if (State == State.Realtime)
            {
                // Look back to find the most recent DFR range when going real-time
                FindMostRecentDFR();
            }
        }

        private void FindMostRecentDFR()
        {
            // Look back through historical bars to find the most recent DFR period (20:00-23:59)
            Print($"{logPrefix}Searching for most recent DFR in historical data...");
            Print($"{logPrefix}Current bar: {CurrentBar}, Available bars to search: {CurrentBar}");
            
            double foundHigh = double.MinValue;
            double foundLow = double.MaxValue;
            bool foundDFR = false;
            DateTime dfrDate = DateTime.MinValue;
            int barsInDFR = 0;
            
            // Calculate bars to search based on timeframe
            // For 2-min: 720 bars = 1 day, For 5-min: 288 bars = 1 day
            int barsPerDay = 1440 / (int)BarsPeriod.Value; // 1440 minutes in a day
            int barsToSearch = Math.Min(CurrentBar, barsPerDay * 3); // Search last 3 days
            
            Print($"{logPrefix}Bar period: {BarsPeriod.Value} minutes, Bars per day: {barsPerDay}");
            Print($"{logPrefix}Searching back {barsToSearch} bars (approx {barsToSearch/barsPerDay} days)");
            
            for (int i = 1; i <= barsToSearch; i++)
            {
                if (i >= CurrentBar) break;
                
                int hour = Time[i].Hour;
                
                // Check if this bar is in DFR period (20:00-23:59)
                if (hour >= 20 && hour <= 23)
                {
                    if (!foundDFR)
                    {
                        // First DFR bar found
                        foundDFR = true;
                        dfrDate = Time[i].Date;
                        foundHigh = High[i];
                        foundLow = Low[i];
                        barsInDFR = 1;
                        Print($"{logPrefix}Found first DFR bar at {Time[i]} - High: {High[i]:F2}, Low: {Low[i]:F2}");
                    }
                    else if (Time[i].Date == dfrDate)
                    {
                        // Same DFR session - update high/low
                        foundHigh = Math.Max(foundHigh, High[i]);
                        foundLow = Math.Min(foundLow, Low[i]);
                        barsInDFR++;
                    }
                    else
                    {
                        // Found an older DFR session, we have the most recent one
                        Print($"{logPrefix}Found older DFR, stopping search. Used {barsInDFR} bars from most recent DFR.");
                        break;
                    }
                }
            }
            
            if (foundDFR && foundHigh != double.MinValue && foundLow != double.MaxValue && barsInDFR > 0)
            {
                activeDFRHigh = foundHigh;
                activeDFRLow = foundLow;
                dfrEstablished = true;
                canTrade = true;
                currentBias = TradeDirection.Bullish; // Start with bullish bias
                currentDayDate = dfrDate.AddDays(1); // Set to the trading day after DFR
                
                Print($"{logPrefix}════════════════════════════════════════");
                Print($"{logPrefix}✓ FOUND DFR from {dfrDate.ToShortDateString()} 20:00-23:59");
                Print($"{logPrefix}DFR High: {activeDFRHigh:F2}");
                Print($"{logPrefix}DFR Low: {activeDFRLow:F2}");
                Print($"{logPrefix}Bars in DFR: {barsInDFR}");
                Print($"{logPrefix}Trading ENABLED with BULLISH bias");
                Print($"{logPrefix}════════════════════════════════════════");
                
                // Draw the DFR levels if enabled
                if (DrawDFRBox)
                    DrawDFRLevels();
            }
            else
            {
                Print($"{logPrefix}════════════════════════════════════════");
                Print($"{logPrefix}⚠ No recent DFR found in historical data");
                Print($"{logPrefix}Searched {barsToSearch} bars back");
                Print($"{logPrefix}Found DFR bars: {barsInDFR}");
                Print($"{logPrefix}Will establish DFR during next 20:00-23:59 period");
                Print($"{logPrefix}════════════════════════════════════════");
            }
        }

        protected override void OnBarUpdate()
        {
            if (State == State.Historical)
                return;
                
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Track DFR period and establish range
            TrackDFRPeriod();
            
            // Check if we're in the no-trade window (17:00-23:59)
            int hour = Time[0].Hour;
            bool isNoTradeWindow = (hour >= 17 && hour <= 23);
            
            if (isNoTradeWindow)
            {
                // Still update bias, but don't trade
                UpdateBiasFromDFR();
                return;
            }
            
            // Update bias based on DFR levels
            UpdateBiasFromDFR();
            
            // Only proceed with trading logic if DFR is established and we can trade
            if (!canTrade || !dfrEstablished)
            {
                if (CurrentBar % 50 == 0)
                    Print($"{logPrefix}Waiting for DFR: canTrade={canTrade}, dfrEstablished={dfrEstablished}, Hour={hour}, Current Bias={currentBias}");
                return;
            }
            
            // Periodic status update
            if (CurrentBar % 100 == 0)
                Print($"{logPrefix}STATUS: Bias={currentBias}, DFR Low={activeDFRLow:F2}, DFR High={activeDFRHigh:F2}, Hour={hour}, Active Trendlines={activeTrendlines.Count}");

            // Detect swings based on current bias
            if (currentBias == TradeDirection.Bullish)
                DetectBullishSwings();
            else
                DetectBearishSwings();

            // Check trendlines for breakout
            CheckTrendlinesForBreakout();
        }

        private void TrackDFRPeriod()
        {
            int hour = Time[0].Hour;
            int minute = Time[0].Minute;
            
            // Check if we're starting a new day (00:00)
            if (hour == 0 && minute == 0)
            {
                if (Time[0].Date != currentDayDate)
                {
                    currentDayDate = Time[0].Date;
                    
                    // Store the previous DFR as the active one
                    if (currentDFRHigh != double.MinValue && currentDFRLow != double.MaxValue)
                    {
                        activeDFRHigh = currentDFRHigh;
                        activeDFRLow = currentDFRLow;
                        dfrEstablished = true;
                        canTrade = true;
                        
                        // Reset bias to Bullish at start of new trading day
                        currentBias = TradeDirection.Bullish;
                        
                        Print($"{logPrefix}════════════════════════════════════════");
                        Print($"{logPrefix}NEW TRADING DAY - {currentDayDate.ToShortDateString()}");
                        Print($"{logPrefix}DFR Range: High={activeDFRHigh:F2}, Low={activeDFRLow:F2}");
                        Print($"{logPrefix}Bias RESET to: BULLISH (default)");
                        Print($"{logPrefix}Trading ENABLED");
                        Print($"{logPrefix}════════════════════════════════════════");
                        
                        // Clear old trendlines for fresh start
                        foreach (var tl in activeTrendlines.ToList())
                            RemoveDrawObject(tl.Tag);
                        activeTrendlines.Clear();
                        swings.Clear();
                        
                        // Draw the DFR box if enabled
                        if (DrawDFRBox)
                            DrawDFRLevels();
                    }
                    else
                    {
                        Print($"{logPrefix}00:00 reached but no DFR established yet (first day)");
                        Print($"{logPrefix}Waiting for DFR period (20:00-23:59) to establish range");
                    }
                }
            }
            
            // Check if we're in the DFR period (20:00 to 23:59 EST)
            bool isDFRTime = (hour >= 20 && hour <= 23);
            
            if (isDFRTime)
            {
                if (!inDFRPeriod)
                {
                    // Starting a new DFR period
                    inDFRPeriod = true;
                    currentDFRStartBar = CurrentBar;
                    currentDFRHigh = High[0];
                    currentDFRLow = Low[0];
                    Print($"{logPrefix}DFR period started at {Time[0]} - High={currentDFRHigh:F2}, Low={currentDFRLow:F2}");
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
                    // Ending the DFR period
                    inDFRPeriod = false;
                    Print($"{logPrefix}DFR period ended at {Time[0]}");
                    Print($"{logPrefix}Final DFR: High={currentDFRHigh:F2}, Low={currentDFRLow:F2}");
                }
            }
        }

        private void UpdateBiasFromDFR()
        {
            if (!dfrEstablished || activeDFRLow == double.MaxValue)
                return;

            // Check the last N bars (user-defined, default 10)
            int barsToCheck = Math.Min(BarsToCheckForBias, CurrentBar);
            bool allBelowDFRLow = true;
            
            // Count how many of the recent bars are below DFR Low
            int barsBelowCount = 0;
            for (int i = 0; i < barsToCheck; i++)
            {
                if (Close[i] < activeDFRLow)
                {
                    barsBelowCount++;
                }
                else
                {
                    allBelowDFRLow = false;
                }
            }

            TradeDirection previousBias = currentBias;
            
            // Bias logic:
            // - Bearish: ALL last N bars close BELOW DFR Low
            // - Bullish: ANY of the last N bars close AT or ABOVE DFR Low
            if (allBelowDFRLow)
            {
                currentBias = TradeDirection.Bearish;
            }
            else
            {
                currentBias = TradeDirection.Bullish;
            }

            // Log bias changes with detailed info
            if (previousBias != currentBias)
            {
                Print($"{logPrefix}");
                Print($"{logPrefix}*** BIAS CHANGE DETECTED ***");
                Print($"{logPrefix}Previous: {previousBias} → Current: {currentBias}");
                Print($"{logPrefix}DFR Low: {activeDFRLow:F2}");
                Print($"{logPrefix}Last {barsToCheck} bars below DFR Low: {barsBelowCount}/{barsToCheck}");
                Print($"{logPrefix}All bars below: {allBelowDFRLow}");
                
                // Show the last N closes for transparency
                StringBuilder closesList = new StringBuilder($"{logPrefix}Recent closes: ");
                for (int i = barsToCheck - 1; i >= 0; i--)
                {
                    closesList.Append($"{Close[i]:F2}");
                    if (i > 0) closesList.Append(", ");
                }
                Print(closesList.ToString());
                Print($"{logPrefix}");
                
                // Clear old trendlines on bias change
                foreach (var tl in activeTrendlines.ToList())
                    RemoveDrawObject(tl.Tag);
                activeTrendlines.Clear();
                swings.Clear();
                
                // Alert for bias change
                string alertMsg = currentBias == TradeDirection.Bullish 
                    ? $"🟢 BULLISH: {Instrument.FullName} | {barsBelowCount}/{barsToCheck} bars broke above DFR Low ({activeDFRLow:F2})"
                    : $"🔴 BEARISH: {Instrument.FullName} | All {barsToCheck} bars below DFR Low ({activeDFRLow:F2})";
                
                Alert("BiasChange_" + CurrentBar, Priority.High, alertMsg, 
                    NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert2.wav", 10, 
                    currentBias == TradeDirection.Bullish ? Brushes.LimeGreen : Brushes.Red, 
                    Brushes.Black);
            }
        }

        private void DrawDFRLevels()
        {
            // Remove old DFR lines
            string tagHigh = "DFR_High";
            string tagLow = "DFR_Low";
            RemoveDrawObject(tagHigh);
            RemoveDrawObject(tagLow);
            RemoveDrawObject(tagHigh + "_Label");
            RemoveDrawObject(tagLow + "_Label");
            
            // Draw horizontal lines for DFR levels extending forward
            Draw.Line(this, tagHigh, false, 10, activeDFRHigh, -50, activeDFRHigh, 
                Brushes.Orange, DashStyleHelper.Dot, 2);
            Draw.Line(this, tagLow, false, 10, activeDFRLow, -50, activeDFRLow, 
                Brushes.Red, DashStyleHelper.Dot, 2);
                
            Draw.Text(this, tagHigh + "_Label", false, "DFR High", 0, activeDFRHigh, 10, 
                Brushes.Orange, new SimpleFont("Arial", 10), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
            Draw.Text(this, tagLow + "_Label", false, "Bias Flip", 0, activeDFRLow, 10, 
                Brushes.Red, new SimpleFont("Arial", 10), TextAlignment.Right, Brushes.Transparent, Brushes.Transparent, 0);
        }

        private void DetectBullishSwings()
        {
            if (!double.IsNaN(swing.SwingHigh[0]))
            {
                double newSwing = swing.SwingHigh[0];
                int swingBar = CurrentBar - swing.SwingHighBar(0, 1, CurrentBar);
                
                if (newSwing != lastSwingValue || swingBar != lastSwingBar)
                {
                    lastSwingValue = newSwing;
                    lastSwingBar = swingBar;
                    
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
            if (!double.IsNaN(swing.SwingLow[0]))
            {
                double newSwing = swing.SwingLow[0];
                int swingBar = CurrentBar - swing.SwingLowBar(0, 1, CurrentBar);
                
                if (newSwing != lastSwingValue || swingBar != lastSwingBar)
                {
                    lastSwingValue = newSwing;
                    lastSwingBar = swingBar;
                    
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
            foreach (var tl in activeTrendlines.ToList())
                RemoveDrawObject(tl.Tag);

            activeTrendlines.Clear();

            for (int i = 0; i < swings.Count - 1; i++)
            {
                var secondLast = swings[i];
                var last = swings[i + 1];

                int deltaBars = last.BarIndex - secondLast.BarIndex;
                if (deltaBars < MinBarsBetweenSwings)
                    continue;

                double slope = (last.Value - secondLast.Value) / deltaBars;
                
                bool validSlope = false;
                if (currentBias == TradeDirection.Bullish)
                {
                    // For bullish: need downward slope (negative)
                    validSlope = slope <= -MinSlopeThreshold;
                }
                else
                {
                    // For bearish: need upward slope (positive)
                    validSlope = slope >= MinSlopeThreshold;
                }
                
                if (!validSlope)
                    continue;

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

                int startBarsAgo = CurrentBar - secondLast.BarIndex;
                double currentY = last.Value + (slope * (CurrentBar - last.BarIndex));
                
                Brush lineColor = currentBias == TradeDirection.Bullish ? Brushes.Blue : Brushes.Red;
                Draw.Line(this, tlData.Tag, false, startBarsAgo, secondLast.Value, 0, currentY,
                    lineColor, DashStyleHelper.Dash, 2);

                Print($"{logPrefix}Drew {currentBias} trendline #{activeTrendlines.Count}: Slope={slope:F4}, Current Y={currentY:F2}");
            }

            while (activeTrendlines.Count > MaxTrendlines)
            {
                RemoveDrawObject(activeTrendlines[0].Tag);
                activeTrendlines.RemoveAt(0);
            }
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
                
                if (currentBias == TradeDirection.Bullish)
                {
                    breakoutOccurred = Close[0] > currentTLValue;
                    wasOnCorrectSide = Close[1] <= prevTLValue;
                }
                else
                {
                    breakoutOccurred = Close[0] < currentTLValue;
                    wasOnCorrectSide = Close[1] >= prevTLValue;
                }
                
                if (wasOnCorrectSide && breakoutOccurred && tl.BarsBroken == 0 && Position.MarketPosition == MarketPosition.Flat)
                {
                    Print($"{logPrefix}");
                    Print($"{logPrefix}🎯 BREAKOUT DETECTED on {tl.Tag}");
                    Print($"{logPrefix}Bias: {currentBias}");
                    Print($"{logPrefix}Prior Close: {Close[1]:F2} | Current Close: {Close[0]:F2}");
                    Print($"{logPrefix}Trendline Value: {currentTLValue:F2}");
                    
                    double stopLossPrice, entryPrice, riskPerContract, targetHalfRR, target1to1;
                    
                    if (currentBias == TradeDirection.Bullish)
                    {
                        stopLossPrice = GetLowestLowSinceBar(tl.EndBar);
                        entryPrice = Close[0];
                        riskPerContract = entryPrice - stopLossPrice;
                        
                        if (stopLossPrice >= entryPrice)
                        {
                            Print($"{logPrefix}❌ Invalid stop: {stopLossPrice:F2} not below entry {entryPrice:F2}");
                            continue;
                        }
                        
                        targetHalfRR = entryPrice + (riskPerContract * 0.5);
                        target1to1 = entryPrice + riskPerContract;
                    }
                    else
                    {
                        stopLossPrice = GetHighestHighSinceBar(tl.EndBar);
                        entryPrice = Close[0];
                        riskPerContract = stopLossPrice - entryPrice;
                        
                        if (stopLossPrice <= entryPrice)
                        {
                            Print($"{logPrefix}❌ Invalid stop: {stopLossPrice:F2} not above entry {entryPrice:F2}");
                            continue;
                        }
                        
                        targetHalfRR = entryPrice - (riskPerContract * 0.5);
                        target1to1 = entryPrice - riskPerContract;
                    }
                    
                    Print($"{logPrefix}Entry: {entryPrice:F2} | Stop: {stopLossPrice:F2} | Risk: {riskPerContract:F2}");
                    Print($"{logPrefix}Target 0.5:1 = {targetHalfRR:F2} | Target 1:1 = {target1to1:F2}");
                    Print($"{logPrefix}");
					
                    // *** NEW: TRADE ENTRY ALERT ***
                    string direction = currentBias == TradeDirection.Bullish ? "LONG" : "SHORT";
                    string entryAlertMsg = $"🚨 {direction} ENTRY | {Instrument.FullName}\n" +
                                          $"Price: {entryPrice:F2} | Stop: {stopLossPrice:F2}\n" +
                                          $"Risk: {riskPerContract:F2} | Targets: {targetHalfRR:F2} / {target1to1:F2}";
                    
                    Alert("TradeEntry_" + CurrentBar, Priority.High, entryAlertMsg, 
                        NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert4.wav", 10, 
                        currentBias == TradeDirection.Bullish ? Brushes.LimeGreen : Brushes.Red, 
                        Brushes.Black);
                    // *** END ALERT ***					
                    
                    // Create unique signal names using CurrentBar to avoid OCO ID conflicts
                    string entry1Signal = $"Entry1_{CurrentBar}";
                    string entry2Signal = $"Entry2_{CurrentBar}";
                    
                    if (currentBias == TradeDirection.Bullish)
                    {
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
                        EnterShort(ContractsHalfRR, entry1Signal);
                        SetStopLoss(entry1Signal, CalculationMode.Price, stopLossPrice, false);
                        SetProfitTarget(entry1Signal, CalculationMode.Price, targetHalfRR);
                        
                        EnterShort(Contracts1to1RR, entry2Signal);
                        SetStopLoss(entry2Signal, CalculationMode.Price, stopLossPrice, false);
                        SetProfitTarget(entry2Signal, CalculationMode.Price, target1to1);
                        
                        Draw.TriangleDown(this, $"Breakout_{CurrentBar}", false, 0, High[0] + (2 * TickSize), Brushes.Red);
                    }
                    
                    Draw.Line(this, $"Stop_{CurrentBar}", false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Red, DashStyleHelper.Solid, 2);
                    Draw.Line(this, $"Target_Half_{CurrentBar}", false, 0, targetHalfRR, 10, targetHalfRR, Brushes.Pink, DashStyleHelper.Dash, 2);
                    Draw.Line(this, $"Target1_{CurrentBar}", false, 0, target1to1, 10, target1to1, Brushes.LimeGreen, DashStyleHelper.Dash, 2);
                }

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

                if (tl.BarsBroken >= BreakoutConfirmBars)
                {
                    Print($"{logPrefix}Expiring broken TL {tl.Tag} after {tl.BarsBroken} bars");
                    RemoveDrawObject(tl.Tag);
                    activeTrendlines.RemoveAt(i);
                    i--;
                }
            }
        }

        private double GetHighestHighSinceBar(int sinceBar)
        {
            double highestHigh = double.MinValue;
            int barsAgo = CurrentBar - sinceBar;
            
            for (int i = 0; i <= barsAgo; i++)
            {
                if (i < CurrentBar && High[i] > highestHigh)
                    highestHigh = High[i];
            }
            
            return highestHigh;
        }

        private double GetLowestLowSinceBar(int sinceBar)
        {
            double lowestLow = double.MaxValue;
            int barsAgo = CurrentBar - sinceBar;
            
            for (int i = 0; i <= barsAgo; i++)
            {
                if (i < CurrentBar && Low[i] < lowestLow)
                    lowestLow = Low[i];
            }
            
            return lowestLow;
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Swing Strength", Description="Swing strength for pivot detection", Order=1, GroupName="Trendline Parameters")]
        public int SwingStrength { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name="Max Trendlines", Description="Maximum trendlines to track", Order=2, GroupName="Trendline Parameters")]
        public int MaxTrendlines { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Min Slope Threshold", Description="Minimum slope for valid trendline (positive value)", Order=3, GroupName="Trendline Parameters")]
        public double MinSlopeThreshold { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name="Min Bars Between Swings", Description="Minimum bars between swings", Order=4, GroupName="Trendline Parameters")]
        public int MinBarsBetweenSwings { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name="Bars To Check For Bias", Description="Number of recent bars that must ALL close below DFR Low for bearish bias", Order=1, GroupName="Bias Detection")]
        public int BarsToCheckForBias { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name="Draw DFR Box", Description="Draw DFR high/low lines on chart", Order=2, GroupName="Bias Detection")]
        public bool DrawDFRBox { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="Contracts 1:1 RR", Description="Contracts for 1:1 risk/reward", Order=1, GroupName="Position Sizing")]
        public int Contracts1to1RR { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="Contracts 0.5:1 RR", Description="Contracts for 0.5:1 risk/reward", Order=2, GroupName="Position Sizing")]
        public int ContractsHalfRR { get; set; }
        #endregion
        
        public enum TradeDirection
        {
            Bullish,
            Bearish
        }
    }
}