#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class SMT90mCycleStrategy : Strategy
    {
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
        private string comp1Ticker;
        private string comp2Ticker;
        private Swing swingIndicator;
        
        private Dictionary<string, double> pendingBearishSSMT = new Dictionary<string, double>();
        private Dictionary<string, double> pendingBullishSSMT = new Dictionary<string, double>();
        
        private double trueDayOpenPrice = 0;
        private DateTime currentDayDate = DateTime.MinValue;
        private int tdoBarIndex = -1;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Strategy that draws lines and alerts on 90m SSMT signals";
                Name = "SMT 90m Cycle Strategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                
                UseOverrideTriad = false;
                Override1 = "";
                Override2 = "";
                BearishSMTColor = Brushes.Red;
                BullishSMTColor = Brushes.Green;
                LineWidth = 2;
                EnableAlerts = true;
                StartHour = 0;
                EndHour = 23;
                SwingStrength = 2;
                EnableTrading = false;
                Contracts = 1;
                RiskRewardRatio = 3.0;
                UseTDOFilter = true;
            }
            else if (State == State.Configure)
            {
                DetermineComparisonTickers();
                
                if (!string.IsNullOrEmpty(comp1Ticker))
                    AddDataSeries(comp1Ticker);
                
                if (!string.IsNullOrEmpty(comp2Ticker))
                    AddDataSeries(comp2Ticker);
            }
            else if (State == State.DataLoaded)
            {
                priorCycleHigh = double.MinValue;
                priorCycleLow = double.MaxValue;
                compPriorCycleHigh = double.MinValue;
                compPriorCycleLow = double.MaxValue;
                comp2PriorCycleHigh = double.MinValue;
                comp2PriorCycleLow = double.MaxValue;
                lastCycle = -1;
                priorCycleHighTime = DateTime.MinValue;
                priorCycleLowTime = DateTime.MinValue;
                
                swingIndicator = Swing(SwingStrength);
            }
        }
        
        private void DetermineComparisonTickers()
        {
            string primaryInstrument = Instrument.FullName;
            
            if (UseOverrideTriad && !string.IsNullOrEmpty(Override1) && !string.IsNullOrEmpty(Override2))
            {
                string contractSuffix = ExtractContractSuffix(primaryInstrument);
                comp1Ticker = Override1 + " " + contractSuffix;
                comp2Ticker = Override2 + " " + contractSuffix;
                Print("Using Override Triad: " + comp1Ticker + " and " + comp2Ticker);
                return;
            }
            
            string suffix = ExtractContractSuffix(primaryInstrument);
            
            if (Regex.IsMatch(primaryInstrument, @"NQ", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "ES " + suffix;
                comp2Ticker = "YM " + suffix;
                Print("Detected NQ - Using Index Triad: ES and YM");
            }
            else if (Regex.IsMatch(primaryInstrument, @"ES", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "NQ " + suffix;
                comp2Ticker = "YM " + suffix;
                Print("Detected ES - Using Index Triad: NQ and YM");
            }
            else if (Regex.IsMatch(primaryInstrument, @"YM", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "NQ " + suffix;
                comp2Ticker = "ES " + suffix;
                Print("Detected YM - Using Index Triad: NQ and ES");
            }
            else if (Regex.IsMatch(primaryInstrument, @"MGC", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "SIL " + suffix;
                comp2Ticker = "PL " + suffix;
                Print("Detected MGC - Using Metals Triad: SIL and PL");
            }
            else if (Regex.IsMatch(primaryInstrument, @"GC", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "SI " + suffix;
                comp2Ticker = "PL " + suffix;
                Print("Detected GC - Using Metals Triad: SI and PL");
            }
            else if (Regex.IsMatch(primaryInstrument, @"SIL", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "MGC " + suffix;
                comp2Ticker = "PL " + suffix;
                Print("Detected SIL - Using Metals Triad: MGC and PL");
            }
            else if (Regex.IsMatch(primaryInstrument, @"SI", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "GC " + suffix;
                comp2Ticker = "PL " + suffix;
                Print("Detected SI - Using Metals Triad: GC and PL");
            }
            else if (Regex.IsMatch(primaryInstrument, @"PL", RegexOptions.IgnoreCase))
            {
                comp1Ticker = "GC " + suffix;
                comp2Ticker = "SI " + suffix;
                Print("Detected PL - Using Metals Triad: GC and SI");
            }
            else
            {
                Print("WARNING: Primary instrument does not match any default triad pattern. Please use Override Triad.");
                comp1Ticker = "";
                comp2Ticker = "";
            }
        }
        
        private string ExtractContractSuffix(string fullName)
        {
            var match = Regex.Match(fullName, @"(\d{2}-\d{2})");
            if (match.Success)
                return match.Groups[1].Value;
            
            match = Regex.Match(fullName, @"(\d{2}\s*-\s*\d{2})");
            if (match.Success)
                return match.Groups[1].Value.Replace(" ", "");
            
            return "12-25";
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 2 || CurrentBars[1] < 2 || CurrentBars[2] < 2)
                return;
            
            // Don't process historical data for trading
            if (State == State.Historical)
                return;
            
            // Track True Day Open (TDO) at 00:00
            if (BarsInProgress == 0)
            {
                DateTime barTime = Time[0];
                if (barTime.Date != currentDayDate && barTime.Hour == 0 && barTime.Minute == 0)
                {
                    currentDayDate = barTime.Date;
                    trueDayOpenPrice = Open[0];
                    Print("True Day Open (TDO) set at " + barTime + " price: " + trueDayOpenPrice);
                }
            }
            
            if (BarsInProgress == 0)
            {
                CheckForSSMTViolations();
            }
            
            int currentCycle = Get90MinuteCycle(Time[0]);
            
            if (currentCycle != lastCycle && lastCycle != -1)
            {
                priorCycleHigh = GetPriorCycleHigh(0);
                priorCycleLow = GetPriorCycleLow(0);
                compPriorCycleHigh = GetPriorCycleHigh(1);
                compPriorCycleLow = GetPriorCycleLow(1);
                comp2PriorCycleHigh = GetPriorCycleHigh(2);
                comp2PriorCycleLow = GetPriorCycleLow(2);
                
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
                priorCycleHigh = GetPriorCycleHigh(0);
                priorCycleLow = GetPriorCycleLow(0);
                compPriorCycleHigh = GetPriorCycleHigh(1);
                compPriorCycleLow = GetPriorCycleLow(1);
                comp2PriorCycleHigh = GetPriorCycleHigh(2);
                comp2PriorCycleLow = GetPriorCycleLow(2);
                
                priorCycleHighTime = GetPriorCycleHighTime();
                priorCycleLowTime = GetPriorCycleLowTime();
            }
            
            lastCycle = currentCycle;
            
            if (BarsInProgress == 0)
            {
                if (High[0] > priorCycleHigh && !primaryBrokeHigh)
                {
                    primaryBrokeHigh = true;
                    CheckForSMTOnBreak(true, 0);
                }
                
                if (Low[0] < priorCycleLow && !primaryBrokeLow)
                {
                    primaryBrokeLow = true;
                    CheckForSMTOnBreak(false, 0);
                }
            }
            
            if (CurrentBars[1] >= 1)
            {
                if (Highs[1][0] > compPriorCycleHigh && !comparisonBrokeHigh)
                {
                    comparisonBrokeHigh = true;
                    CheckForSMTOnBreak(true, 1);
                }
                
                if (Lows[1][0] < compPriorCycleLow && !comparisonBrokeLow)
                {
                    comparisonBrokeLow = true;
                    CheckForSMTOnBreak(false, 1);
                }
            }
            
            if (CurrentBars[2] >= 1)
            {
                if (Highs[2][0] > comp2PriorCycleHigh && !comparison2BrokeHigh)
                {
                    comparison2BrokeHigh = true;
                    CheckForSMTOnBreak(true, 2);
                }
                
                if (Lows[2][0] < comp2PriorCycleLow && !comparison2BrokeLow)
                {
                    comparison2BrokeLow = true;
                    CheckForSMTOnBreak(false, 2);
                }
            }
        }
        
        private void CheckForSMTOnBreak(bool isHighBreak, int whichInstrument)
        {
            if (isHighBreak)
            {
                if (whichInstrument > 0 && !primaryBrokeHigh)
                {
                    string compTicker = (whichInstrument == 1 ? comp1Ticker : comp2Ticker);
                    string label = ExtractSymbolOnly(compTicker) + " 90SSMT";
                    DrawSMTLine(true, priorCycleHighTime, true, label, compTicker);
                }
                else if (whichInstrument == 0 && !comparisonBrokeHigh && !comparison2BrokeHigh)
                {
                    DrawSMTLine(true, priorCycleHighTime, false, "", "");
                }
            }
            else
            {
                if (whichInstrument > 0 && !primaryBrokeLow)
                {
                    string compTicker = (whichInstrument == 1 ? comp1Ticker : comp2Ticker);
                    string label = ExtractSymbolOnly(compTicker) + " 90SSMT";
                    DrawSMTLine(false, priorCycleLowTime, true, label, compTicker);
                }
                else if (whichInstrument == 0 && !comparisonBrokeLow && !comparison2BrokeLow)
                {
                    DrawSMTLine(false, priorCycleLowTime, false, "", "");
                }
            }
        }
        
        private string ExtractSymbolOnly(string fullTicker)
        {
            if (string.IsNullOrEmpty(fullTicker))
                return "";
            
            var parts = fullTicker.Split(' ');
            if (parts.Length >= 2)
                return parts[0] + "-" + parts[1];
            return fullTicker;
        }
        
        private void DrawSMTLine(bool isBearish, DateTime startTime, bool showLabel, string labelText, string compTicker)
        {
            if (startTime == DateTime.MinValue)
                return;
            
            int currentHour = Time[0].Hour;
            bool withinTimeframe = IsWithinTimeframe(currentHour);
            
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
            
            string tag = "SMT90m_" + (isBearish ? "Bear_" : "Bull_") + Time[0].ToString("yyyyMMddHHmmss");
            
            string alertMsg = "";
            
            if (isBearish)
            {
                alertMsg = "BEARISH 90m SSMT: " + Instrument.FullName + 
                    (showLabel ? " diverged from " + compTicker : " broke high alone");
                
                if (!withinTimeframe)
                {
                    alertMsg += " - SSMT found but NOT in user timeframe (" + StartHour + ":00 - " + EndHour + ":00). Current hour: " + currentHour + ":00";
                    Print(Time[0] + " - " + alertMsg);
                    return;
                }
                
                Draw.Line(this, tag, false, 
                    startBar, High[startBar], 
                    0, High[0], 
                    BearishSMTColor, DashStyleHelper.Solid, LineWidth);
                
                Draw.Line(this, tag + "_Marker", false,
                    0, Low[0],
                    -4, Low[0],
                    BearishSMTColor, DashStyleHelper.Dot, 1);
                
                Draw.Text(this, tag + "_TCISD", "tcisd", -2, Low[0] - (5 * TickSize), BearishSMTColor);
                
                pendingBearishSSMT[tag] = Low[0];
                Print("Bearish SSMT detected - waiting for close below " + Low[0] + " to mark swing point");
                
                if (showLabel)
                {
                    int midBar = startBar / 2;
                    double midPrice = (High[startBar] + High[0]) / 2;
                    Draw.Text(this, tag + "_Label", labelText, midBar, midPrice + (20 * TickSize), BearishSMTColor);
                }
                
                Print(Time[0] + " - " + alertMsg);
                
                if (EnableAlerts)
                    Alert("SMTAlert", Priority.High, alertMsg, "", 10, Brushes.Red, Brushes.White);
            }
            else
            {
                alertMsg = "BULLISH 90m SSMT: " + Instrument.FullName + 
                    (showLabel ? " diverged from " + compTicker : " broke low alone");
                
                if (!withinTimeframe)
                {
                    alertMsg += " - SSMT found but NOT in user timeframe (" + StartHour + ":00 - " + EndHour + ":00). Current hour: " + currentHour + ":00";
                    Print(Time[0] + " - " + alertMsg);
                    return;
                }
                
                Draw.Line(this, tag, false, 
                    startBar, Low[startBar], 
                    0, Low[0], 
                    BullishSMTColor, DashStyleHelper.Solid, LineWidth);
                
                Draw.Line(this, tag + "_Marker", false,
                    0, High[0],
                    -4, High[0],
                    BullishSMTColor, DashStyleHelper.Dot, 1);
                
                Draw.Text(this, tag + "_TCISD", "tcisd", -2, High[0] + (5 * TickSize), BullishSMTColor);
                
                pendingBullishSSMT[tag] = High[0];
                Print("Bullish SSMT detected - waiting for close above " + High[0] + " to mark swing point");
                
                if (showLabel)
                {
                    int midBar = startBar / 2;
                    double midPrice = (Low[startBar] + Low[0]) / 2;
                    Draw.Text(this, tag + "_Label", labelText, midBar, midPrice - (20 * TickSize), BullishSMTColor);
                }
                
                Print(Time[0] + " - " + alertMsg);
                
                if (EnableAlerts)
                    Alert("SMTAlert", Priority.High, alertMsg, "", 10, Brushes.Green, Brushes.White);
            }
        }
        
        private void CheckForSSMTViolations()
        {
            // Check if current time is within trading timeframe
            int currentHour = Time[0].Hour;
            bool withinTimeframe = IsWithinTimeframe(currentHour);
            
            List<string> bearishToRemove = new List<string>();
            foreach (var kvp in pendingBearishSSMT)
            {
                string tag = kvp.Key;
                double tcisdLow = kvp.Value;
                
                if (Close[0] < tcisdLow)
                {
                    Print("Bearish SSMT violated! Close[0]=" + Close[0] + " below tcisd " + tcisdLow + " at " + Time[0] + " - marking swing point");
                    
                    Brush color = BearishSMTColor;
                    
                    int swingHighBar = FindLastSwingHigh();
                    if (swingHighBar >= 0)
                    {
                        double swingHigh = High[swingHighBar];
                        Print("Drawing swing marker at bar " + swingHighBar + " (Time: " + Time[swingHighBar] + ") high: " + swingHigh);
                        Draw.ArrowDown(this, tag + "_SwingMarker", false, swingHighBar, swingHigh + (10 * TickSize), color);
                        
                        // Check TDO filter for bearish trade
                        bool tdoFilterPass = true;
                        if (UseTDOFilter && trueDayOpenPrice > 0)
                        {
                            tdoFilterPass = tcisdLow > trueDayOpenPrice;
                            if (!tdoFilterPass)
                            {
                                Print("TDO Filter FAILED for Bearish trade: tcisd (" + tcisdLow + ") must be ABOVE TDO (" + trueDayOpenPrice + ")");
                            }
                            else
                            {
                                Print("TDO Filter PASSED for Bearish trade: tcisd (" + tcisdLow + ") is ABOVE TDO (" + trueDayOpenPrice + ")");
                            }
                        }
                        
                        // Only enter trade if within timeframe and TDO filter passes
                        if (EnableTrading && Position.MarketPosition == MarketPosition.Flat && withinTimeframe && tdoFilterPass)
                        {
                            double entryPrice = Close[0];
                            double stopLoss = swingHigh;
                            double risk = Math.Abs(entryPrice - stopLoss);
                            double reward = risk * RiskRewardRatio;
                            double takeProfit = entryPrice - reward;
                            
                            Print("BEARISH TRADE SETUP: Entry=" + entryPrice + " SL=" + stopLoss + " TP=" + takeProfit + " Risk=" + risk + " Reward=" + reward + " R:R=" + RiskRewardRatio);
                            
                            EnterShortLimit(0, true, Contracts, entryPrice, tag + "_ShortEntry");
                            SetStopLoss(tag + "_ShortEntry", CalculationMode.Price, stopLoss, false);
                            SetProfitTarget(tag + "_ShortEntry", CalculationMode.Price, takeProfit);
                        }
                        else if (EnableTrading && Position.MarketPosition != MarketPosition.Flat)
                        {
                            Print("BEARISH SSMT violation detected but already in a " + Position.MarketPosition + " position - no new trade");
                        }
                        else if (EnableTrading && !withinTimeframe)
                        {
                            Print("BEARISH SSMT violation detected but outside trading timeframe (hour " + currentHour + ") - no trade");
                        }
                    }
                    else
                    {
                        Print("Could not find swing high for bearish SSMT violation");
                    }
                    
                    bearishToRemove.Add(tag);
                }
            }
            
            foreach (string tag in bearishToRemove)
            {
                pendingBearishSSMT.Remove(tag);
            }
            
            List<string> bullishToRemove = new List<string>();
            foreach (var kvp in pendingBullishSSMT)
            {
                string tag = kvp.Key;
                double tcisdHigh = kvp.Value;
                
                if (Close[0] > tcisdHigh)
                {
                    Print("Bullish SSMT violated! Close[0]=" + Close[0] + " above tcisd " + tcisdHigh + " at " + Time[0] + " - marking swing point");
                    
                    Brush color = BullishSMTColor;
                    
                    int swingLowBar = FindLastSwingLow();
                    if (swingLowBar >= 0)
                    {
                        double swingLow = Low[swingLowBar];
                        Print("Drawing swing marker at bar " + swingLowBar + " (Time: " + Time[swingLowBar] + ") low: " + swingLow);
                        Draw.ArrowUp(this, tag + "_SwingMarker", false, swingLowBar, swingLow - (10 * TickSize), color);
                        
                        // Check TDO filter for bullish trade
                        bool tdoFilterPass = true;
                        if (UseTDOFilter && trueDayOpenPrice > 0)
                        {
                            tdoFilterPass = tcisdHigh < trueDayOpenPrice;
                            if (!tdoFilterPass)
                            {
                                Print("TDO Filter FAILED for Bullish trade: tcisd (" + tcisdHigh + ") must be BELOW TDO (" + trueDayOpenPrice + ")");
                            }
                            else
                            {
                                Print("TDO Filter PASSED for Bullish trade: tcisd (" + tcisdHigh + ") is BELOW TDO (" + trueDayOpenPrice + ")");
                            }
                        }
                        
                        // Only enter trade if within timeframe and TDO filter passes
                        if (EnableTrading && Position.MarketPosition == MarketPosition.Flat && withinTimeframe && tdoFilterPass)
                        {
                            double entryPrice = Close[0];
                            double stopLoss = swingLow;
                            double risk = Math.Abs(entryPrice - stopLoss);
                            double reward = risk * RiskRewardRatio;
                            double takeProfit = entryPrice + reward;
                            
                            Print("BULLISH TRADE SETUP: Entry=" + entryPrice + " SL=" + stopLoss + " TP=" + takeProfit + " Risk=" + risk + " Reward=" + reward + " R:R=" + RiskRewardRatio);
                            
                            EnterLongLimit(0, true, Contracts, entryPrice, tag + "_LongEntry");
                            SetStopLoss(tag + "_LongEntry", CalculationMode.Price, stopLoss, false);
                            SetProfitTarget(tag + "_LongEntry", CalculationMode.Price, takeProfit);
                        }
                        else if (EnableTrading && Position.MarketPosition != MarketPosition.Flat)
                        {
                            Print("BULLISH SSMT violation detected but already in a " + Position.MarketPosition + " position - no new trade");
                        }
                        else if (EnableTrading && !withinTimeframe)
                        {
                            Print("BULLISH SSMT violation detected but outside trading timeframe (hour " + currentHour + ") - no trade");
                        }
                    }
                    else
                    {
                        Print("Could not find swing low for bullish SSMT violation");
                    }
                    
                    bullishToRemove.Add(tag);
                }
            }
            
            foreach (string tag in bullishToRemove)
            {
                pendingBullishSSMT.Remove(tag);
            }
        }
        
        private int FindLastSwingHigh()
        {
            for (int i = 1; i < Math.Min(CurrentBar, 200); i++)
            {
                double swingHighValue = swingIndicator.SwingHigh[i];
                if (swingHighValue != 0 && !double.IsNaN(swingHighValue))
                {
                    for (int j = i; j <= Math.Min(i + SwingStrength * 2 + 1, CurrentBar - 1); j++)
                    {
                        if (Math.Abs(High[j] - swingHighValue) < 0.01)
                        {
                            Print("Found swing high at bar " + j + " with value " + swingHighValue);
                            return j;
                        }
                    }
                    int estimatedBar = i + SwingStrength;
                    Print("Using estimated swing high at bar " + estimatedBar + " for value " + swingHighValue);
                    return estimatedBar;
                }
            }
            Print("No swing high found in last 200 bars");
            return -1;
        }
        
        private int FindLastSwingLow()
        {
            for (int i = 1; i < Math.Min(CurrentBar, 200); i++)
            {
                double swingLowValue = swingIndicator.SwingLow[i];
                if (swingLowValue != 0 && !double.IsNaN(swingLowValue))
                {
                    for (int j = i; j <= Math.Min(i + SwingStrength * 2 + 1, CurrentBar - 1); j++)
                    {
                        if (Math.Abs(Low[j] - swingLowValue) < 0.01)
                        {
                            Print("Found swing low at bar " + j + " with value " + swingLowValue);
                            return j;
                        }
                    }
                    int estimatedBar = i + SwingStrength;
                    Print("Using estimated swing low at bar " + estimatedBar + " for value " + swingLowValue);
                    return estimatedBar;
                }
            }
            Print("No swing low found in last 200 bars");
            return -1;
        }
        
        private bool IsWithinTimeframe(int hour)
        {
            if (StartHour <= EndHour)
            {
                return hour >= StartHour && hour <= EndHour;
            }
            else
            {
                return hour >= StartHour || hour <= EndHour;
            }
        }
        
        private int Get90MinuteCycle(DateTime time)
        {
            TimeSpan t = time.TimeOfDay;
            int hour = t.Hours;
            int minute = t.Minutes;
            
            if (hour >= 18 && hour <= 23)
            {
                if (hour == 18 || (hour == 19 && minute < 30)) return 1;
                if ((hour == 19 && minute >= 30) || (hour == 20 && minute < 60)) return 2;
                if (hour == 21 || (hour == 22 && minute < 30)) return 3;
                if ((hour == 22 && minute >= 30) || hour == 23) return 4;
            }
            else if (hour >= 0 && hour <= 5)
            {
                if (hour == 0 || (hour == 1 && minute < 30)) return 5;
                if ((hour == 1 && minute >= 30) || (hour == 2 && minute < 60)) return 6;
                if (hour == 3 || (hour == 4 && minute < 30)) return 7;
                if ((hour == 4 && minute >= 30) || hour == 5) return 8;
            }
            else if (hour >= 6 && hour <= 11)
            {
                if (hour == 6 || (hour == 7 && minute < 30)) return 9;
                if ((hour == 7 && minute >= 30) || (hour == 8 && minute < 60)) return 10;
                if (hour == 9 || (hour == 10 && minute < 30)) return 11;
                if ((hour == 10 && minute >= 30) || hour == 11) return 12;
            }
            else if (hour >= 12 && hour <= 16)
            {
                if (hour == 12 || (hour == 13 && minute < 30)) return 13;
                if ((hour == 13 && minute >= 30) || (hour == 14 && minute < 60)) return 14;
                if (hour == 15 || (hour == 16 && minute < 30)) return 15;
                if ((hour == 16 && minute >= 30)) return 16;
            }
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
            if (priorCycle == 0) priorCycle = 16;
            
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
            if (priorCycle == 0) priorCycle = 16;
            
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
        [Display(Name = "Use Override Triad", Description = "Check to manually specify comparison instruments", Order = 1, GroupName = "Comparison Assets")]
        public bool UseOverrideTriad { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Override 1", Description = "First override symbol (e.g., ES, GC) - leave empty for default", Order = 2, GroupName = "Comparison Assets")]
        public string Override1 { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Override 2", Description = "Second override symbol (e.g., YM, SI) - leave empty for default", Order = 3, GroupName = "Comparison Assets")]
        public string Override2 { get; set; }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bearish SMT Color", Description = "Color for bearish SMT lines", Order = 4, GroupName = "Display")]
        public Brush BearishSMTColor { get; set; }
        
        [Browsable(false)]
        public string BearishSMTColorSerializable
        {
            get { return Serialize.BrushToString(BearishSMTColor); }
            set { BearishSMTColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bullish SMT Color", Description = "Color for bullish SMT lines", Order = 5, GroupName = "Display")]
        public Brush BullishSMTColor { get; set; }
        
        [Browsable(false)]
        public string BullishSMTColorSerializable
        {
            get { return Serialize.BrushToString(BullishSMTColor); }
            set { BullishSMTColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Line Width", Description = "Width of SMT lines", Order = 6, GroupName = "Display")]
        public int LineWidth { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Alerts", Description = "Enable audio/visual alerts for SMT signals", Order = 7, GroupName = "Display")]
        public bool EnableAlerts { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Start Hour", Description = "Start hour for SSMT display (0-23). Use 0 and 23 for all hours.", Order = 8, GroupName = "Time Filter")]
        public int StartHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "End Hour", Description = "End hour for SSMT display (0-23). Use 0 and 23 for all hours.", Order = 9, GroupName = "Time Filter")]
        public int EndHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Swing Strength", Description = "Strength parameter for swing points (default 2)", Order = 10, GroupName = "Swing Settings")]
        public int SwingStrength { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Trading", Description = "Enable automatic trade entries on SSMT violations", Order = 11, GroupName = "Trading")]
        public bool EnableTrading { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts", Description = "Number of contracts to trade", Order = 12, GroupName = "Trading")]
        public int Contracts { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "Risk:Reward Ratio", Description = "Risk to reward ratio (e.g., 3.0 for 3:1)", Order = 13, GroupName = "Trading")]
        public double RiskRewardRatio { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use TDO Filter", Description = "Enable True Day Open filter (Bearish: tcisd above TDO, Bullish: tcisd below TDO)", Order = 14, GroupName = "Trading")]
        public bool UseTDOFilter { get; set; }
        #endregion
    }
}