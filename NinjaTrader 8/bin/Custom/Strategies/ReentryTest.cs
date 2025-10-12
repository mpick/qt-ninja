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
    // Enum for direction
    public enum TradeDirection
    {
        Long,
        Short
    }

    public class ReEntryTest : Strategy
    {
        private string entrySignalName = "Entry";
        private bool waitingForRecovery = false; // Per-instance: true after own SL, until recovery
        private bool needsInitialSubmit = true; // New: Controls one-time initial submit per load

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Quantity", Description="Number of contracts", Order=1, GroupName="Parameters")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Entry Price", Description="Limit entry price", Order=2, GroupName="Parameters")]
        public double EntryPrice { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Take Profit Price", Description="Absolute TP price (above entry for long, below for short)", Order=3, GroupName="Parameters")]
        public double TakeProfitPrice { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Direction", Description="Trade direction", Order=4, GroupName="Parameters")]
        public TradeDirection Direction { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Stop Loss Ticks", Description="Stop loss offset in ticks (0 to disable)", Order=5, GroupName="Parameters")]
        public int StopLossTicks { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Dynamic limit entry with TP; re-enters after TP hit. After own SL, waits for price recovery. Supports multi-instance scaling.";
                Name = "ReEntryTest";
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
                TimeInForce = TimeInForce.Day; // Fixed for futures
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                // Default parameter values
                Quantity = 1;
                EntryPrice = 239.99;
                TakeProfitPrice = 240.50;
                Direction = TradeDirection.Long;
                StopLossTicks = 0; // Disabled by default
            }
            else if (State == State.DataLoaded)
            {
                waitingForRecovery = false;
                needsInitialSubmit = true; // Reset for fresh initial submit
                // Validate params
                if ((Direction == TradeDirection.Long && TakeProfitPrice <= EntryPrice) ||
                    (Direction == TradeDirection.Short && TakeProfitPrice >= EntryPrice))
                {
                    Print("Warning: TP price may be invalid for selected direction.");
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

            // Check for recovery after own SL
            if (waitingForRecovery)
            {
                bool recoveryMet = false;
                if (Direction == TradeDirection.Long)
                {
                    if (Close[0] > EntryPrice)
                    {
                        recoveryMet = true;
                        Print(Time[0] + " - Price recovered above entry (" + EntryPrice + "). Resuming trading.");
                    }
                }
                else // Short
                {
                    if (Close[0] < EntryPrice)
                    {
                        recoveryMet = true;
                        Print(Time[0] + " - Price recovered below entry (" + EntryPrice + "). Resuming trading.");
                    }
                }
                if (recoveryMet)
                {
                    waitingForRecovery = false;
                    needsInitialSubmit = true; // Allow submit after recovery
                    SubmitEntryAndExits();
                }
                else
                {
                    return; // Still waiting, skip
                }
            }

            // Initial submit (only once per load, if not waiting)
            if (needsInitialSubmit && !waitingForRecovery)
            {
                SubmitEntryAndExits();
                needsInitialSubmit = false;
            }
        }

        // Helper method to submit entry + TP/SL (avoids code duplication)
        private void SubmitEntryAndExits()
        {
            // Submit entry based on direction
            if (Direction == TradeDirection.Long)
            {
                EnterLongLimit(Quantity, EntryPrice, entrySignalName);
            }
            else
            {
                EnterShortLimit(Quantity, EntryPrice, entrySignalName);
            }

            // Attach TP
            SetProfitTarget(entrySignalName, CalculationMode.Price, TakeProfitPrice, false);

            // Optional: Attach stop loss if enabled
            if (StopLossTicks > 0)
            {
                double slPrice = Direction == TradeDirection.Long
                    ? EntryPrice - (StopLossTicks * TickSize)
                    : EntryPrice + (StopLossTicks * TickSize);
                SetStopLoss(entrySignalName, CalculationMode.Price, slPrice, false);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null) return;

            // Detect own SL fill (per-instance via FromEntrySignal) and set waiting flag
            if (execution.Order.FromEntrySignal == entrySignalName && execution.Name == "Stop loss")
            {
                waitingForRecovery = true;
                Print(Time[0] + " - Own SL hit at " + price + ". Waiting for price recovery to resume trading.");
                return; // Exit early, no immediate re-entry
            }

            // Detect own TP fill (per-instance) and re-submit duplicate entry + TP (immediate, unless waiting)
            if (execution.Order.FromEntrySignal == entrySignalName && execution.Name == "Profit target" && !waitingForRecovery)
            {
                // Immediate re-entry after own TP
                SubmitEntryAndExits();
                Print(Time[0] + " - Own TP hit at " + price + ". New " + Direction.ToString() + " entry limit placed at " + EntryPrice);
            }
        }
    }
}
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
    // Enum for direction
    public enum TradeDirection
    {
        Long,
        Short
    }

    public class ReEntryTest : Strategy
    {
        private string entrySignalName = "Entry";
        private bool waitingForRecovery = false; // Per-instance: true after own SL, until recovery
        private bool needsInitialSubmit = true; // New: Controls one-time initial submit per load

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Quantity", Description="Number of contracts", Order=1, GroupName="Parameters")]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Entry Price", Description="Limit entry price", Order=2, GroupName="Parameters")]
        public double EntryPrice { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Take Profit Price", Description="Absolute TP price (above entry for long, below for short)", Order=3, GroupName="Parameters")]
        public double TakeProfitPrice { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Direction", Description="Trade direction", Order=4, GroupName="Parameters")]
        public TradeDirection Direction { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Stop Loss Ticks", Description="Stop loss offset in ticks (0 to disable)", Order=5, GroupName="Parameters")]
        public int StopLossTicks { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Dynamic limit entry with TP; re-enters after TP hit. After own SL, waits for price recovery. Supports multi-instance scaling.";
                Name = "ReEntryTest";
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
                TimeInForce = TimeInForce.Day; // Fixed for futures
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;
                // Default parameter values
                Quantity = 1;
                EntryPrice = 239.99;
                TakeProfitPrice = 240.50;
                Direction = TradeDirection.Long;
                StopLossTicks = 0; // Disabled by default
            }
            else if (State == State.DataLoaded)
            {
                waitingForRecovery = false;
                needsInitialSubmit = true; // Reset for fresh initial submit
                // Validate params
                if ((Direction == TradeDirection.Long && TakeProfitPrice <= EntryPrice) ||
                    (Direction == TradeDirection.Short && TakeProfitPrice >= EntryPrice))
                {
                    Print("Warning: TP price may be invalid for selected direction.");
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

            // Check for recovery after own SL
            if (waitingForRecovery)
            {
                bool recoveryMet = false;
                if (Direction == TradeDirection.Long)
                {
                    if (Close[0] > EntryPrice)
                    {
                        recoveryMet = true;
                        Print(Time[0] + " - Price recovered above entry (" + EntryPrice + "). Resuming trading.");
                    }
                }
                else // Short
                {
                    if (Close[0] < EntryPrice)
                    {
                        recoveryMet = true;
                        Print(Time[0] + " - Price recovered below entry (" + EntryPrice + "). Resuming trading.");
                    }
                }
                if (recoveryMet)
                {
                    waitingForRecovery = false;
                    needsInitialSubmit = true; // Allow submit after recovery
                    SubmitEntryAndExits();
                }
                else
                {
                    return; // Still waiting, skip
                }
            }

            // Initial submit (only once per load, if not waiting)
            if (needsInitialSubmit && !waitingForRecovery)
            {
                SubmitEntryAndExits();
                needsInitialSubmit = false;
            }
        }

        // Helper method to submit entry + TP/SL (avoids code duplication)
        private void SubmitEntryAndExits()
        {
            // Submit entry based on direction
            if (Direction == TradeDirection.Long)
            {
                EnterLongLimit(Quantity, EntryPrice, entrySignalName);
            }
            else
            {
                EnterShortLimit(Quantity, EntryPrice, entrySignalName);
            }

            // Attach TP
            SetProfitTarget(entrySignalName, CalculationMode.Price, TakeProfitPrice, false);

            // Optional: Attach stop loss if enabled
            if (StopLossTicks > 0)
            {
                double slPrice = Direction == TradeDirection.Long
                    ? EntryPrice - (StopLossTicks * TickSize)
                    : EntryPrice + (StopLossTicks * TickSize);
                SetStopLoss(entrySignalName, CalculationMode.Price, slPrice, false);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null) return;

            // Detect own SL fill (per-instance via FromEntrySignal) and set waiting flag
            if (execution.Order.FromEntrySignal == entrySignalName && execution.Name == "Stop loss")
            {
                waitingForRecovery = true;
                Print(Time[0] + " - Own SL hit at " + price + ". Waiting for price recovery to resume trading.");
                return; // Exit early, no immediate re-entry
            }

            // Detect own TP fill (per-instance) and re-submit duplicate entry + TP (immediate, unless waiting)
            if (execution.Order.FromEntrySignal == entrySignalName && execution.Name == "Profit target" && !waitingForRecovery)
            {
                // Immediate re-entry after own TP
                SubmitEntryAndExits();
                Print(Time[0] + " - Own TP hit at " + price + ". New " + Direction.ToString() + " entry limit placed at " + EntryPrice);
            }
        }
    }
}