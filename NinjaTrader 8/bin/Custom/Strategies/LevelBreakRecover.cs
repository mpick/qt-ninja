using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media; // For Brushes
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui.Chart; // For DashStyleHelper
using NinjaTrader.Gui; // For additional Gui support

namespace NinjaTrader.NinjaScript.Strategies
{
    public class LevelPullbackStrategy : Strategy
    {
        private bool waitingForConfirm;
        private bool entrySignalActive; // Flag to track active entry signal for resubmission
        private int barsSinceEntrySignal; // Counter for bars since signal activation

        public enum DirectionType
        {
            Long,
            Short
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Places a limit order at the specified level after consecutive bars on one side followed by consecutive bars on opposite side.";
                Name = "LevelPullbackStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = true; // CHANGED: Enable for order-related debug logs
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                // Default inputs
                Quantity = 1;
                Level = 0;
                Direction = DirectionType.Long;
                ConsecTriggerBars = 2;
                ConsecConfirmBars = 1;
                TP_Ticks = 20;
                SL_Ticks = 10;
                MaxBarsForOrder = 90; // Default to 90 bars for order expiration
            }
            else if (State == State.DataLoaded)
            {
                waitingForConfirm = false;
                entrySignalActive = false;
                barsSinceEntrySignal = 0; // Initialize counter
            }
        }

        protected override void OnBarUpdate()
        {
            // Draw the horizontal line at the Level on the first bar (static visual reference)
            if (CurrentBar == 0)
            {
                Draw.HorizontalLine(this, "LevelLine", false, Level, Brushes.Black, DashStyleHelper.Dash, 1);
            }

            if (BarsInProgress != 0)
                return;
            if (CurrentBar < Math.Max(ConsecTriggerBars, ConsecConfirmBars))
            {
                // NEW: Debug early return
                Print($"Bar {CurrentBar}: Skipping - Insufficient bars ({CurrentBar} < {Math.Max(ConsecTriggerBars, ConsecConfirmBars)})");
                return;
            }
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // NEW: Debug position check
                Print($"Bar {CurrentBar}: Skipping - Not flat (Position: {Position.MarketPosition})");
                return;
            }

            bool isLong = (Direction == DirectionType.Long);

            // NEW: Debug current values
            Print($"Bar {CurrentBar} | Close[0]: {Close[0]:F4} | Level: {Level:F4} | Direction: {Direction} | waitingForConfirm: {waitingForConfirm} | entrySignalActive: {entrySignalActive} | barsSinceEntrySignal: {barsSinceEntrySignal}");

            // Define conditions based on direction
            Func<int, bool> triggerCondition = (int bar) =>
            {
                if (isLong)
                    return Close[bar] < Level;
                else
                    return Close[bar] > Level;
            };

            Func<int, bool> confirmCondition = (int bar) =>
            {
                if (isLong)
                    return Close[bar] > Level;
                else
                    return Close[bar] < Level;
            };

            // Check for trigger condition (consecutive bars on trigger side)
            bool allTrigger = true;
            for (int i = 0; i < ConsecTriggerBars; i++)
            {
                bool thisTrigger = triggerCondition(i);
                Print($"Bar {CurrentBar}: Trigger check for bar {i}: Close[{i}]={Close[i]:F4} { (thisTrigger ? "✓" : "✗") } (expected: {(isLong ? "<" : ">")} {Level:F4})");
                if (!thisTrigger)
                {
                    allTrigger = false;
                    break;
                }
            }

            if (allTrigger)
            {
                waitingForConfirm = true;
                // Reset signal if we re-enter trigger state (avoids stale orders)
                entrySignalActive = false;
                barsSinceEntrySignal = 0; // Reset counter on new trigger
                Print($"Bar {CurrentBar}: TRIGGER MET! Waiting for {ConsecConfirmBars} confirm bar(s).");
                return;
            }

            // If waiting for confirm
            if (waitingForConfirm)
            {
                // Check for confirm condition (consecutive bars on confirm side)
                bool allConfirm = true;
                for (int i = 0; i < ConsecConfirmBars; i++)
                {
                    bool thisConfirm = confirmCondition(i);
                    Print($"Bar {CurrentBar}: Confirm check for bar {i}: Close[{i}]={Close[i]:F4} { (thisConfirm ? "✓" : "✗") } (expected: {(isLong ? ">" : "<")} {Level:F4})");
                    if (!thisConfirm)
                    {
                        allConfirm = false;
                        break;
                    }
                }

                if (allConfirm)
                {
                    // Place the limit order
                    string signalName = isLong ? "LongEntry" : "ShortEntry";
                    Print($"Bar {CurrentBar}: CONFIRM MET! Placing { (isLong ? "Long" : "Short") } limit order at {Level:F4}.");
                    if (isLong)
                        EnterLongLimit(Quantity, Level, signalName);
                    else
                        EnterShortLimit(Quantity, Level, signalName);
                    // Set TP and SL in ticks from entry
                    SetProfitTarget(signalName, CalculationMode.Ticks, TP_Ticks);
                    SetStopLoss(signalName, CalculationMode.Ticks, SL_Ticks, false);
                    waitingForConfirm = false;
                    entrySignalActive = true; // Activate signal for resubmission
                    barsSinceEntrySignal = 0; // Reset counter on signal activation
                }
                else
                {
                    // If current bar is back on trigger side, reset
                    if (!confirmCondition(0))
                    {
                        waitingForConfirm = false;
                        Print($"Bar {CurrentBar}: Confirm failed - Current bar back on trigger side. Resetting wait.");
                    }
                    else
                    {
                        Print($"Bar {CurrentBar}: Partial confirm fail - Not all {ConsecConfirmBars} bars met condition. Still waiting.");
                    }
                }
            }

            // Resubmit entry signal every bar to keep order alive (while flat and active)
            if (entrySignalActive && Position.MarketPosition == MarketPosition.Flat)
            {
                barsSinceEntrySignal++; // Increment counter each resubmission
                // Check if exceeded max bars; if so, expire the signal (stops resubmission, causing cancel at bar close)
                if (barsSinceEntrySignal > MaxBarsForOrder)
                {
                    entrySignalActive = false;
                    barsSinceEntrySignal = 0;
                    Print($"Bar {CurrentBar}: Order expired after {MaxBarsForOrder} bars. Cancelling pending order.");
                    return; // Exit early to avoid resubmission
                }

                string signalName = isLong ? "LongEntry" : "ShortEntry";
                Print($"Bar {CurrentBar}: Resubmitting {signalName} (bars since: {barsSinceEntrySignal}/{MaxBarsForOrder}).");
                if (isLong)
                    EnterLongLimit(Quantity, Level, signalName);
                else
                    EnterShortLimit(Quantity, Level, signalName);
                // Re-attach TP/SL every resubmission (NT requires this for managed approach)
                SetProfitTarget(signalName, CalculationMode.Ticks, TP_Ticks);
                SetStopLoss(signalName, CalculationMode.Ticks, SL_Ticks, false);
            }
            else if (entrySignalActive)
            {
                // NEW: Debug if signal active but not resubmitting
                Print($"Bar {CurrentBar}: Entry signal active but not resubmitting (Position: {Position.MarketPosition}).");
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Description = "Number of contracts to trade per entry", Order = 0, GroupName = "Parameters")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level", Description = "The price level for the limit order", Order = 1, GroupName = "Parameters")]
        public double Level { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Direction", Description = "Trade direction (Long waits for below then above; Short waits for above then below)", Order = 2, GroupName = "Parameters")]
        public DirectionType Direction { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Consec Trigger Bars", Description = "Consecutive bars below level (for Long) or above (for Short) to trigger waiting state. Default: 2", Order = 3, GroupName = "Parameters")]
        public int ConsecTriggerBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Consec Confirm Bars", Description = "Consecutive bars above level (for Long) or below (for Short) to confirm and place order. Default: 1", Order = 4, GroupName = "Parameters")]
        public int ConsecConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Ticks", Description = "Take profit in ticks from entry level", Order = 5, GroupName = "Parameters")]
        public int TP_Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SL Ticks", Description = "Stop loss in ticks from entry level", Order = 6, GroupName = "Parameters")]
        public int SL_Ticks { get; set; }

        // Property for max bars before order expires
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Bars For Order", Description = "Maximum bars to keep the limit order active if unfilled (then expires). Default: 90", Order = 7, GroupName = "Parameters")]
        public int MaxBarsForOrder { get; set; }
        #endregion
    }
}
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media; // For Brushes
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui.Chart; // For DashStyleHelper
using NinjaTrader.Gui; // For additional Gui support

namespace NinjaTrader.NinjaScript.Strategies
{
    public class LevelPullbackStrategy : Strategy
    {
        private bool waitingForConfirm;
        private bool entrySignalActive; // Flag to track active entry signal for resubmission
        private int barsSinceEntrySignal; // Counter for bars since signal activation

        public enum DirectionType
        {
            Long,
            Short
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Places a limit order at the specified level after consecutive bars on one side followed by consecutive bars on opposite side.";
                Name = "LevelPullbackStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = true; // CHANGED: Enable for order-related debug logs
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                // Default inputs
                Quantity = 1;
                Level = 0;
                Direction = DirectionType.Long;
                ConsecTriggerBars = 2;
                ConsecConfirmBars = 1;
                TP_Ticks = 20;
                SL_Ticks = 10;
                MaxBarsForOrder = 90; // Default to 90 bars for order expiration
            }
            else if (State == State.DataLoaded)
            {
                waitingForConfirm = false;
                entrySignalActive = false;
                barsSinceEntrySignal = 0; // Initialize counter
            }
        }

        protected override void OnBarUpdate()
        {
            // Draw the horizontal line at the Level on the first bar (static visual reference)
            if (CurrentBar == 0)
            {
                Draw.HorizontalLine(this, "LevelLine", false, Level, Brushes.Black, DashStyleHelper.Dash, 1);
            }

            if (BarsInProgress != 0)
                return;
            if (CurrentBar < Math.Max(ConsecTriggerBars, ConsecConfirmBars))
            {
                // NEW: Debug early return
                Print($"Bar {CurrentBar}: Skipping - Insufficient bars ({CurrentBar} < {Math.Max(ConsecTriggerBars, ConsecConfirmBars)})");
                return;
            }
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // NEW: Debug position check
                Print($"Bar {CurrentBar}: Skipping - Not flat (Position: {Position.MarketPosition})");
                return;
            }

            bool isLong = (Direction == DirectionType.Long);

            // NEW: Debug current values
            Print($"Bar {CurrentBar} | Close[0]: {Close[0]:F4} | Level: {Level:F4} | Direction: {Direction} | waitingForConfirm: {waitingForConfirm} | entrySignalActive: {entrySignalActive} | barsSinceEntrySignal: {barsSinceEntrySignal}");

            // Define conditions based on direction
            Func<int, bool> triggerCondition = (int bar) =>
            {
                if (isLong)
                    return Close[bar] < Level;
                else
                    return Close[bar] > Level;
            };

            Func<int, bool> confirmCondition = (int bar) =>
            {
                if (isLong)
                    return Close[bar] > Level;
                else
                    return Close[bar] < Level;
            };

            // Check for trigger condition (consecutive bars on trigger side)
            bool allTrigger = true;
            for (int i = 0; i < ConsecTriggerBars; i++)
            {
                bool thisTrigger = triggerCondition(i);
                Print($"Bar {CurrentBar}: Trigger check for bar {i}: Close[{i}]={Close[i]:F4} { (thisTrigger ? "✓" : "✗") } (expected: {(isLong ? "<" : ">")} {Level:F4})");
                if (!thisTrigger)
                {
                    allTrigger = false;
                    break;
                }
            }

            if (allTrigger)
            {
                waitingForConfirm = true;
                // Reset signal if we re-enter trigger state (avoids stale orders)
                entrySignalActive = false;
                barsSinceEntrySignal = 0; // Reset counter on new trigger
                Print($"Bar {CurrentBar}: TRIGGER MET! Waiting for {ConsecConfirmBars} confirm bar(s).");
                return;
            }

            // If waiting for confirm
            if (waitingForConfirm)
            {
                // Check for confirm condition (consecutive bars on confirm side)
                bool allConfirm = true;
                for (int i = 0; i < ConsecConfirmBars; i++)
                {
                    bool thisConfirm = confirmCondition(i);
                    Print($"Bar {CurrentBar}: Confirm check for bar {i}: Close[{i}]={Close[i]:F4} { (thisConfirm ? "✓" : "✗") } (expected: {(isLong ? ">" : "<")} {Level:F4})");
                    if (!thisConfirm)
                    {
                        allConfirm = false;
                        break;
                    }
                }

                if (allConfirm)
                {
                    // Place the limit order
                    string signalName = isLong ? "LongEntry" : "ShortEntry";
                    Print($"Bar {CurrentBar}: CONFIRM MET! Placing { (isLong ? "Long" : "Short") } limit order at {Level:F4}.");
                    if (isLong)
                        EnterLongLimit(Quantity, Level, signalName);
                    else
                        EnterShortLimit(Quantity, Level, signalName);
                    // Set TP and SL in ticks from entry
                    SetProfitTarget(signalName, CalculationMode.Ticks, TP_Ticks);
                    SetStopLoss(signalName, CalculationMode.Ticks, SL_Ticks, false);
                    waitingForConfirm = false;
                    entrySignalActive = true; // Activate signal for resubmission
                    barsSinceEntrySignal = 0; // Reset counter on signal activation
                }
                else
                {
                    // If current bar is back on trigger side, reset
                    if (!confirmCondition(0))
                    {
                        waitingForConfirm = false;
                        Print($"Bar {CurrentBar}: Confirm failed - Current bar back on trigger side. Resetting wait.");
                    }
                    else
                    {
                        Print($"Bar {CurrentBar}: Partial confirm fail - Not all {ConsecConfirmBars} bars met condition. Still waiting.");
                    }
                }
            }

            // Resubmit entry signal every bar to keep order alive (while flat and active)
            if (entrySignalActive && Position.MarketPosition == MarketPosition.Flat)
            {
                barsSinceEntrySignal++; // Increment counter each resubmission
                // Check if exceeded max bars; if so, expire the signal (stops resubmission, causing cancel at bar close)
                if (barsSinceEntrySignal > MaxBarsForOrder)
                {
                    entrySignalActive = false;
                    barsSinceEntrySignal = 0;
                    Print($"Bar {CurrentBar}: Order expired after {MaxBarsForOrder} bars. Cancelling pending order.");
                    return; // Exit early to avoid resubmission
                }

                string signalName = isLong ? "LongEntry" : "ShortEntry";
                Print($"Bar {CurrentBar}: Resubmitting {signalName} (bars since: {barsSinceEntrySignal}/{MaxBarsForOrder}).");
                if (isLong)
                    EnterLongLimit(Quantity, Level, signalName);
                else
                    EnterShortLimit(Quantity, Level, signalName);
                // Re-attach TP/SL every resubmission (NT requires this for managed approach)
                SetProfitTarget(signalName, CalculationMode.Ticks, TP_Ticks);
                SetStopLoss(signalName, CalculationMode.Ticks, SL_Ticks, false);
            }
            else if (entrySignalActive)
            {
                // NEW: Debug if signal active but not resubmitting
                Print($"Bar {CurrentBar}: Entry signal active but not resubmitting (Position: {Position.MarketPosition}).");
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Quantity", Description = "Number of contracts to trade per entry", Order = 0, GroupName = "Parameters")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Level", Description = "The price level for the limit order", Order = 1, GroupName = "Parameters")]
        public double Level { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Direction", Description = "Trade direction (Long waits for below then above; Short waits for above then below)", Order = 2, GroupName = "Parameters")]
        public DirectionType Direction { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Consec Trigger Bars", Description = "Consecutive bars below level (for Long) or above (for Short) to trigger waiting state. Default: 2", Order = 3, GroupName = "Parameters")]
        public int ConsecTriggerBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Consec Confirm Bars", Description = "Consecutive bars above level (for Long) or below (for Short) to confirm and place order. Default: 1", Order = 4, GroupName = "Parameters")]
        public int ConsecConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Ticks", Description = "Take profit in ticks from entry level", Order = 5, GroupName = "Parameters")]
        public int TP_Ticks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SL Ticks", Description = "Stop loss in ticks from entry level", Order = 6, GroupName = "Parameters")]
        public int SL_Ticks { get; set; }

        // Property for max bars before order expires
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Bars For Order", Description = "Maximum bars to keep the limit order active if unfilled (then expires). Default: 90", Order = 7, GroupName = "Parameters")]
        public int MaxBarsForOrder { get; set; }
        #endregion
    }
}