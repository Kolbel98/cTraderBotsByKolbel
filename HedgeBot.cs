using System;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HedgeBot : Robot
    {
        // ====== PARAMETRY ======
        [Parameter("Label", DefaultValue = "HedgeBot", Group = "General")]
        public string BotLabel { get; set; }

        [Parameter("Hedge Distance (pips)", DefaultValue = 40.0, Group = "Hedge")]
        public double HedgeDistancePips { get; set; }

        [Parameter("TR (hedge multiplier)", DefaultValue = 2.0, Group = "Hedge")]
        public double TrMultiplier { get; set; }

        [Parameter("Starting Volume (lots)", DefaultValue = 0.10, Group = "Manual Panel")]
        public double StartingLots { get; set; }

        [Parameter("Initial Profit Target $", DefaultValue = 40.0, Group = "Targets")]
        public double InitialProfitTargetUsd { get; set; }

        [Parameter("Hedge Profit Target $", DefaultValue = 0.0, Group = "Targets")]
        public double HedgeProfitTargetUsd { get; set; }

        [Parameter("Risk $ (global stop)", DefaultValue = 100.0, Group = "Protection")]
        public double RiskUsd { get; set; }

        [Parameter("Rounding", DefaultValue = RoundingMode.ToNearest, Group = "Advanced")]
        public RoundingMode VolumeRounding { get; set; }

        // ====== STAN ======
        private bool hedgeTriggered;              // włączamy gdy pierwsze przeciwne zlecenie zostanie WYPEŁNIONE
        private double nextHedgeLots;             // kolejny wolumen w LOTACH (zawsze mnożony przez TR)
        private TradeType lastOpenedSide;         // kierunek ostatnio otwartej pozycji (by ustawić kolejne pending)

        // UI
        private TextBox lotsInput;
        private ComboBox sideCombo;

        // ====== START ======
        protected override void OnStart()
        {
            // Subskrypcje zdarzeń
            Positions.Opened += OnPositionsOpened;
            Positions.Closed += OnPositionsClosed;
            PendingOrders.Filled += OnPendingOrderFilled;
            PendingOrders.Cancelled += OnPendingOrderCancelled;

            BuildUi(); // panel do ręcznego startu (działa również w Visual Backtesting)
        }

        // ====== TICK: logika targetów / risk ======
        protected override void OnTick()
        {
            var myPositions = Positions.FindAll(BotLabel, SymbolName);
            if (myPositions.Length == 0)
                return;

            double net = myPositions.Sum(p => p.NetProfit);

            // Globalny "hard stop"
            if (net <= -Math.Abs(RiskUsd))
            {
                Print("Risk hit: {0:F2} ≤ -{1:F2}. Closing ALL.", net, Math.Abs(RiskUsd));
                CloseAllAndCancelPendings();
                ResetState();
                return;
            }

            // Targety: przed hedgem -> Initial; po hedgu -> Hedge target
            if (!hedgeTriggered)
            {
                if (net >= InitialProfitTargetUsd)
                {
                    Print("Initial target hit: {0:F2} ≥ {1:F2}. Closing ALL.", net, InitialProfitTargetUsd);
                    CloseAllAndCancelPendings();
                    ResetState();
                }
            }
            else
            {
                if (net >= HedgeProfitTargetUsd)
                {
                    Print("Hedge target hit: {0:F2} ≥ {1:F2}. Closing ALL.", net, HedgeProfitTargetUsd);
                    CloseAllAndCancelPendings();
                    ResetState();
                }
            }
        }

        // ====== ZDARZENIA ======

        // 1) Każde otwarcie pozycji (start manualny lub fill z pendingu)
        private void OnPositionsOpened(PositionOpenedEventArgs args)
        {
            var pos = args.Position;
            if (pos.SymbolName != SymbolName) return; // ten sam symbol
            if (pos.Label != BotLabel) return;        // tylko nasze „sesje”

            lastOpenedSide = pos.TradeType;

            // Jeśli to pierwsza pozycja w sesji -> ustaw pierwszy hedge pending
            var myPositions = Positions.FindAll(BotLabel, SymbolName);
            if (myPositions.Length == 1 && !hedgeTriggered)
            {
                // pierwszy pending: wolumen = (loty pozycji startowej) * TR
                double baseLots = Symbol.VolumeInUnitsToQuantity(pos.VolumeInUnits);
                nextHedgeLots = baseLots * TrMultiplier;

                PlaceOppositePendingFrom(pos.EntryPrice, pos.TradeType, nextHedgeLots);
                return;
            }

            // Jeśli to nie pierwsza (czyli wypełniło się jakieś pending) – nic nie robimy tutaj,
            // kolejne pendingi dołoży handler PendingOrders.Filled (dokładniejsza informacja o fillu).
        }

        // 2) Pending został WYPEŁNIONY -> otwarto nową pozycję
        private void OnPendingOrderFilled(PendingOrderFilledEventArgs args)
        {
            var po = args.PendingOrder;
            if (po.SymbolName != SymbolName || po.Label != BotLabel) return;

            // Od teraz hedge jest aktywny (są pozycje obu kierunków)
            hedgeTriggered = true;

            // Wypełniło się np. SELL STOP -> ustaw kolejne BUY STOP na poziomie "entry tej świeżej pozycji ± distance"
            // Użyjemy ceny targetu pendingu (po.TargetPrice) jako odniesienia do siatki,
            // a wolumen kolejnego pendingu = poprzedni * TR.
            var oppositeOfFilled = Opposite(po.TradeType);
            nextHedgeLots = Math.Abs(nextHedgeLots * TrMultiplier);

            PlaceOppositePendingFrom(po.TargetPrice, po.TradeType, nextHedgeLots);
        }

        // 3) Pending anulowany (np. trzymamy porządek)
        private void OnPendingOrderCancelled(PendingOrderCancelledEventArgs args)
        {
            // nic krytycznego – zostawiamy informacyjnie
            if (args.PendingOrder.Label == BotLabel && args.PendingOrder.SymbolName == SymbolName)
                Print("Pending cancelled ({0}) reason: {1}", args.PendingOrder.TradeType, args.Reason);
        }

        private void OnPositionsClosed(PositionClosedEventArgs args)
        {
            // Jeśli trader ręcznie zamknie wszystko, zresetuj stan i wyczyść pendingi
            if (args.Position.SymbolName != SymbolName || args.Position.Label != BotLabel) return;

            var left = Positions.FindAll(BotLabel, SymbolName).Length;
            if (left == 0)
            {
                CancelAllPendings();
                ResetState();
            }
        }

        // ====== POMOCNICZE: stawianie pendingów, otwieranie ręczne, zamykanie ======

        private void PlaceOppositePendingFrom(double referencePrice, TradeType referenceSide, double lotsForPending)
        {
            var distPrice = HedgeDistancePips * Symbol.PipSize;

            TradeType pendingSide;
            double targetPrice;

            if (referenceSide == TradeType.Buy)
            {
                // Do longa stawiamy SELL STOP poniżej
                pendingSide = TradeType.Sell;
                targetPrice = referencePrice - distPrice;
            }
            else
            {
                // Do shorta stawiamy BUY STOP powyżej
                pendingSide = TradeType.Buy;
                targetPrice = referencePrice + distPrice;
            }

            double volUnits = Symbol.QuantityToVolumeInUnits(lotsForPending);
            volUnits = Symbol.NormalizeVolumeInUnits(volUnits, VolumeRounding);

            var res = PlaceStopOrder(pendingSide, SymbolName, volUnits, targetPrice, BotLabel);
            if (!res.IsSuccessful)
                Print("Failed to place pending ({0}) @ {1}: {2}", pendingSide, targetPrice, res.Error);
            else
                Print("Placed {0} STOP @ {1}, lots={2:F2}", pendingSide, targetPrice, lotsForPending);
        }

        private void OpenManual(TradeType side, double lots)
        {
            double units = Symbol.QuantityToVolumeInUnits(lots);
            units = Symbol.NormalizeVolumeInUnits(units, VolumeRounding);

            var res = ExecuteMarketOrder(side, SymbolName, units, BotLabel);
            if (!res.IsSuccessful)
                Print("Open {0} failed: {1}", side, res.Error);
            else
                Print("Opened {0} {1:F2} lots @ {2}", side, lots, res.Position?.EntryPrice);
        }

        private void CloseAllAndCancelPendings()
        {
            foreach (var p in Positions.FindAll(BotLabel, SymbolName))
                ClosePosition(p);

            CancelAllPendings();
        }

        private void CancelAllPendings()
        {
            foreach (var o in PendingOrders.Where(o => o.Label == BotLabel && o.SymbolName == SymbolName))
                CancelPendingOrder(o);
        }

        private void ResetState()
        {
            hedgeTriggered = false;
            nextHedgeLots = 0;
        }

        private static TradeType Opposite(TradeType side) => side == TradeType.Buy ? TradeType.Sell : TradeType.Buy;

        // ====== UI (Visual Backtest friendly) ======
        private void BuildUi()
        {
            // Pasek: kierunek + input lots + 3 przyciski
            var bar = new StackPanel { BackgroundColor = Color.Gray,
                 HorizontalAlignment = HorizontalAlignment.Left,
                 VerticalAlignment = VerticalAlignment.Top,
                 Width = 200,
                 Margin = 20
                  };

            sideCombo = new ComboBox { Width = 80, Margin = 5 };
            sideCombo.AddItem("BUY");
            sideCombo.AddItem("SELL");
            sideCombo.SelectedIndex = 0;

            lotsInput = new TextBox { Width = 80, Margin = 5, Text = StartingLots.ToString(CultureInfo.InvariantCulture) };

            var openBtn = new Button { Text = "OPEN", Width = 70, Height = 24, Margin = 5 };
            var closeBtn = new Button { Text = "CLOSE ALL", Width = 100, Height = 24, Margin = 5 };
            var cancelBtn = new Button { Text = "CANCEL PENDS", Width = 120, Height = 24, Margin = 5 };

            openBtn.Click += _ =>
            {
                if (!double.TryParse(lotsInput.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var lots) || lots <= 0)
                {
                    Print("Bad lots input.");
                    return;
                }
                var side = sideCombo.SelectedIndex == 0 ? TradeType.Buy : TradeType.Sell;
                OpenManual(side, lots);
            };

            closeBtn.Click += _ => CloseAllAndCancelPendings();
            cancelBtn.Click += _ => CancelAllPendings();

            bar.AddChild(sideCombo);
            bar.AddChild(lotsInput);
            bar.AddChild(openBtn);
            bar.AddChild(closeBtn);
            bar.AddChild(cancelBtn);

            Chart.AddControl(bar);
        }
    }
}

