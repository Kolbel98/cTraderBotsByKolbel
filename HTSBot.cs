using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class TrendFollowingBot : Robot
    {
        #region Basic Settings
        [Parameter("Bot Label", DefaultValue = "TrendBot", Group = "Basic Settings")]
        public string BotLabel { get; set; }

        [Parameter("Short MA Period", DefaultValue = 33, Group = "Basic Settings")]
        public int ShortMaPeriod { get; set; }

        [Parameter("Long MA Period", DefaultValue = 144, Group = "Basic Settings")]
        public int LongMaPeriod { get; set; }

        // UWAGA: brak DefaultValue dla TimeFrame (nie jest wspierane przez atrybut)
        [Parameter("Time Frame", Group = "Basic Settings")]
        public TimeFrame MaTimeFrame { get; set; }

        [Parameter("Volume (Lots)", DefaultValue = 0.01, Group = "Basic Settings")]
        public double VolumeInLots { get; set; }

        [Parameter("Max Positions", DefaultValue = 1, Group = "Basic Settings")]
        public int MaxPositions { get; set; }
        #endregion

        #region Trade Settings
        public enum TradeDirection { Both, OnlyLong, OnlyShort }

        [Parameter("Trade Direction", DefaultValue = TradeDirection.Both, Group = "Trade Settings")]
        public TradeDirection TradeDirectionOption { get; set; }

        public enum OpeningMethod { ShortChannel, LongChannel, CrossChannel, BothChannel, Manual }

        [Parameter("Opening Method", DefaultValue = OpeningMethod.ShortChannel, Group = "Trade Settings")]
        public OpeningMethod OpeningMethodOption { get; set; }

        [Parameter("Pyramiding (pips)", DefaultValue = 200, Group = "Trade Settings")]
        public double PyramidingStepPips { get; set; }
        #endregion

        #region Profit / Stop Loss Settings
        public enum SLTPMode { Position, Global }

        public enum ClosingMethod { Standard, SL_Cross_Short_Channel, SL_Cross_Long_Channel }

        [Parameter("Closing Method", DefaultValue = ClosingMethod.Standard, Group = "Profit/SL Settings")]
        public ClosingMethod ClosingMethodOption { get; set; }

        [Parameter("Take Profit ($)", DefaultValue = 50, Group = "Profit/SL Settings")]
        public double TakeProfitAmount { get; set; }

        [Parameter("Stop Loss ($)", DefaultValue = 50, Group = "Profit/SL Settings")]
        public double StopLossAmount { get; set; }

        [Parameter("Close on Trend Change", DefaultValue = true, Group = "Profit/SL Settings")]
        public bool EnableCloseOnTrendChange { get; set; }

        [Parameter("SL/TP Mode", DefaultValue = SLTPMode.Position, Group = "Profit/SL Settings")]
        public SLTPMode SLTPModeOption { get; set; }

        [Parameter("Global Take Profit ($)", DefaultValue = 1000, Group = "Profit/SL Settings")]
        public double GlobalTakeProfitAmount { get; set; }

        [Parameter("Global Stop Loss ($)", DefaultValue = 1000, Group = "Profit/SL Settings")]
        public double GlobalStopLossAmount { get; set; }
        #endregion

        #region Stop Loss Options
        [Parameter("Use Break Even", DefaultValue = false, Group = "Stop Loss Options")]
        public bool UseBreakEven { get; set; }

        [Parameter("Break Even Profit ($)", DefaultValue = 10, Group = "Stop Loss Options")]
        public double BreakEvenProfit { get; set; }

        [Parameter("Use Trailing Stop", DefaultValue = false, Group = "Stop Loss Options")]
        public bool UseTrailingStop { get; set; }

        [Parameter("Trailing Stop Activation ($)", DefaultValue = 5, Group = "Stop Loss Options")]
        public double TrailingStopActivation { get; set; }

        [Parameter("Trailing Stop Distance (pips)", DefaultValue = 20, Group = "Stop Loss Options")]
        public double TrailingStopDistance { get; set; }
        #endregion

       

        #region Indicators and Bars
        private WellesWilderSmoothing shortMaLow;
        private WellesWilderSmoothing shortMaHigh;
        private WellesWilderSmoothing longMaLow;
        private WellesWilderSmoothing longMaHigh;
        private Bars maBars;
        #endregion

        protected override void OnStart()
        {
            maBars = MarketData.GetBars(MaTimeFrame); // bierze bieżący symbol

            shortMaLow  = Indicators.WellesWilderSmoothing(maBars.LowPrices,  ShortMaPeriod);
            shortMaHigh = Indicators.WellesWilderSmoothing(maBars.HighPrices, ShortMaPeriod);
            longMaLow   = Indicators.WellesWilderSmoothing(maBars.LowPrices,  LongMaPeriod);
            longMaHigh  = Indicators.WellesWilderSmoothing(maBars.HighPrices, LongMaPeriod);
        }

        protected override void OnTick()
        {
            var index = maBars.Count - 1;
            if (index < Math.Max(ShortMaPeriod, LongMaPeriod) + 1)
                return;

            CheckAndClosePositions();

            if (SLTPModeOption == SLTPMode.Global)
                CheckAndCloseAllPositions();

            if (UseBreakEven)
                AdjustStopLossToBreakEven();

            if (UseTrailingStop)
                AdjustTrailingStop();


            bool isUpTrend =
                shortMaLow.Result[index]  > longMaLow.Result[index] &&
                shortMaHigh.Result[index] > longMaHigh.Result[index];

            bool isDownTrend =
                shortMaLow.Result[index]  < longMaLow.Result[index] &&
                shortMaHigh.Result[index] < longMaHigh.Result[index];

            bool canOpenLong  = TradeDirectionOption == TradeDirection.Both || TradeDirectionOption == TradeDirection.OnlyLong;
            bool canOpenShort = TradeDirectionOption == TradeDirection.Both || TradeDirectionOption == TradeDirection.OnlyShort;

            if (isUpTrend && canOpenLong)
                OpenPosition(index, TradeType.Buy);
            else if (isDownTrend && canOpenShort)
                OpenPosition(index, TradeType.Sell);
        }

        private void AdjustStopLossToBreakEven()
        {
            var positions = Positions.FindAll(BotLabel, SymbolName);
            foreach (var position in positions)
            {
                if (position.NetProfit >= BreakEvenProfit)
                {
                    double? be = position.EntryPrice;
                    ModifyPosition(position, be, position.TakeProfit, ProtectionType.Absolute);
                }
            }
        }

        private void AdjustTrailingStop()
        {
            var positions = Positions.FindAll(BotLabel, SymbolName);
            foreach (var position in positions)
            {
                if (position.NetProfit < TrailingStopActivation)
                    continue;

                if (position.TradeType == TradeType.Buy)
                {
                    double newStop = Symbol.Bid - TrailingStopDistance * Symbol.PipSize;
                    if (!position.StopLoss.HasValue || newStop > position.StopLoss.Value)
                        ModifyPosition(position, newStop, position.TakeProfit, ProtectionType.Absolute);
                }
                else
                {
                    double newStop = Symbol.Ask + TrailingStopDistance * Symbol.PipSize;
                    if (!position.StopLoss.HasValue || newStop < position.StopLoss.Value)
                        ModifyPosition(position, newStop, position.TakeProfit, ProtectionType.Absolute);
                }
            }
        }

        

        private void OpenPosition(int index, TradeType tradeType)
        {
            bool openingConditionMet = false;

            switch (OpeningMethodOption)
            {
                case OpeningMethod.ShortChannel:
                    if (tradeType == TradeType.Buy &&
                        maBars.LowPrices[index - 1] <= shortMaLow.Result[index - 1] &&
                        maBars.LowPrices[index] > shortMaLow.Result[index])
                        openingConditionMet = true;
                    else if (tradeType == TradeType.Sell &&
                             maBars.HighPrices[index - 1] >= shortMaHigh.Result[index - 1] &&
                             maBars.HighPrices[index] < shortMaHigh.Result[index])
                        openingConditionMet = true;
                    break;

                case OpeningMethod.LongChannel:
                    if (tradeType == TradeType.Buy &&
                        maBars.ClosePrices[index - 1] <= longMaLow.Result[index - 1] &&
                        maBars.ClosePrices[index] > longMaLow.Result[index])
                        openingConditionMet = true;
                    else if (tradeType == TradeType.Sell &&
                             maBars.ClosePrices[index - 1] >= longMaHigh.Result[index - 1] &&
                             maBars.ClosePrices[index] < longMaHigh.Result[index])
                        openingConditionMet = true;
                    break;

                case OpeningMethod.CrossChannel:
                    if (tradeType == TradeType.Buy &&
                        shortMaLow.Result[index - 1] <= longMaLow.Result[index - 1] &&
                        shortMaLow.Result[index] > longMaLow.Result[index])
                        openingConditionMet = true;
                    else if (tradeType == TradeType.Sell &&
                             shortMaHigh.Result[index - 1] >= longMaHigh.Result[index - 1] &&
                             shortMaHigh.Result[index] < longMaHigh.Result[index])
                        openingConditionMet = true;
                    break;

                case OpeningMethod.BothChannel:
                    if (tradeType == TradeType.Buy)
                    {
                        bool condShort = maBars.LowPrices[index - 1] <= shortMaLow.Result[index - 1] &&
                                         maBars.LowPrices[index] > shortMaLow.Result[index];
                        bool condLong  = maBars.ClosePrices[index - 1] <= longMaLow.Result[index - 1] &&
                                         maBars.ClosePrices[index] > longMaLow.Result[index];
                        if (condShort || condLong) openingConditionMet = true;
                    }
                    else
                    {
                        bool condShort = maBars.HighPrices[index - 1] >= shortMaHigh.Result[index - 1] &&
                                         maBars.HighPrices[index] < shortMaHigh.Result[index];
                        bool condLong  = maBars.ClosePrices[index - 1] >= longMaHigh.Result[index - 1] &&
                                         maBars.ClosePrices[index] < longMaHigh.Result[index];
                        if (condShort || condLong) openingConditionMet = true;
                    }
                    break;

                case OpeningMethod.Manual:
                    break;
            }

            if (!openingConditionMet)
                return;

            var sameSide = Positions.FindAll(BotLabel, SymbolName)
                                    .Where(p => p.TradeType == tradeType)
                                    .ToArray();

            int openPositions = sameSide.Length;
            if (openPositions >= MaxPositions)
                return;

            bool canOpenNewPosition = false;
            if (openPositions == 0)
            {
                canOpenNewPosition = true;
            }
            else
            {
                var lastPosition = sameSide.OrderByDescending(p => p.EntryTime).FirstOrDefault();
                if (lastPosition != null)
                {
                    double priceDiff = tradeType == TradeType.Buy
                        ? Symbol.Bid - lastPosition.EntryPrice
                        : lastPosition.EntryPrice - Symbol.Ask;

                    double priceDiffPips = priceDiff / Symbol.PipSize;
                    if (priceDiffPips >= PyramidingStepPips)
                        canOpenNewPosition = true;
                }
            }

            if (!canOpenNewPosition)
                return;

            double volumeUnits = Symbol.QuantityToVolumeInUnits(VolumeInLots);
            ExecuteMarketOrder(tradeType, SymbolName, volumeUnits, BotLabel);
        }

        private void CheckAndClosePositions()
        {
            var index = maBars.Count - 1;
            if (index < Math.Max(ShortMaPeriod, LongMaPeriod) + 1)
                return;

            bool isUpTrend =
                shortMaLow.Result[index]  > longMaLow.Result[index] &&
                shortMaHigh.Result[index] > longMaHigh.Result[index];

            bool isDownTrend =
                shortMaLow.Result[index]  < longMaLow.Result[index] &&
                shortMaHigh.Result[index] < longMaHigh.Result[index];

            var positions = Positions.FindAll(BotLabel, SymbolName);
            foreach (var position in positions)
            {
                double netProfit = position.NetProfit;

                if (netProfit >= TakeProfitAmount)
                {
                    ClosePosition(position);
                    continue;
                }
                else if (netProfit <= -StopLossAmount)
                {
                    ClosePosition(position);
                    continue;
                }

                if (EnableCloseOnTrendChange)
                {
                    if (position.TradeType == TradeType.Buy && isDownTrend)
                    {
                        ClosePosition(position);
                        continue;
                    }
                    else if (position.TradeType == TradeType.Sell && isUpTrend)
                    {
                        ClosePosition(position);
                        continue;
                    }
                }

                switch (ClosingMethodOption)
                {
                    case ClosingMethod.SL_Cross_Short_Channel:
                        if (position.TradeType == TradeType.Buy)
                        {
                            if (maBars.ClosePrices[index - 1] >= shortMaLow.Result[index - 1] &&
                                maBars.ClosePrices[index] < shortMaLow.Result[index])
                            {
                                ClosePosition(position);
                                continue;
                            }
                        }
                        else
                        {
                            if (maBars.ClosePrices[index - 1] <= shortMaHigh.Result[index - 1] &&
                                maBars.ClosePrices[index] > shortMaHigh.Result[index])
                            {
                                ClosePosition(position);
                                continue;
                            }
                        }
                        break;

                    case ClosingMethod.SL_Cross_Long_Channel:
                        if (position.TradeType == TradeType.Buy)
                        {
                            if (maBars.ClosePrices[index - 1] >= longMaLow.Result[index - 1] &&
                                maBars.ClosePrices[index] < longMaLow.Result[index])
                            {
                                ClosePosition(position);
                                continue;
                            }
                        }
                        else
                        {
                            if (maBars.ClosePrices[index - 1] <= longMaHigh.Result[index - 1] &&
                                maBars.ClosePrices[index] > longMaHigh.Result[index])
                            {
                                ClosePosition(position);
                                continue;
                            }
                        }
                        break;

                    case ClosingMethod.Standard:
                    default:
                        break;
                }
            }
        }

        private void CheckAndCloseAllPositions()
        {
            var myPositions = Positions.FindAll(BotLabel, SymbolName);
            double totalNetProfit = myPositions.Sum(pos => pos.NetProfit);

            if (totalNetProfit >= GlobalTakeProfitAmount || totalNetProfit <= -GlobalStopLossAmount)
            {
                foreach (var position in myPositions)
                    ClosePosition(position);
            }
        }
    }
}


