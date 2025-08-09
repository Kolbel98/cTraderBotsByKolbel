

## ​ HTSBot

**What it does:**  
A fully automated trend-following bot that uses Welles–Wilder smoothed moving averages to trade in the direction of price momentum.

**Key Features:**
- Uses modern `Bars` API and `WellesWilderSmoothing` for cleaner and efficient code.
- Fully configurable via input parameters (MA periods, TimeFrame, volume, trade direction, SL/TP).
- Advanced trade management: Break-even, Trailing Stop, Global SL/TP thresholds.
- Built-in **hedge mode**—activates when trend reverses, protecting open trades.
- Interactive UI panel (works in **Visual Backtesting**) to open trades manually.

---

##  HedgeBot

**Purpose:**  
Protects your manual trades by dynamically managing counter-positions. Automatically places counter-trades at a safe distance, scaling them up until the overall trade is neutralized—or triggered by a hard stop.

**Workflow:**
1. You open a trade manually.
2. Bot places a **counter stop-order** at *X pip distance*, with size = last position × **TR**.
3. On fill, increments and places the next counter in the opposite direction, with volume × TR.
4. Closes all positions when:
   - Initial target reached (before hedge).
   - Hedge profit target achieved (after hedge activation).
   - Hard stop (Risk $) hit.

**Interface:**
- Visual panel: Choose side (BUY/SELL), lot size, and action buttons (OPEN, CLOSE ALL, CANCEL PENDINGS).
- Fully functional in **Visual Backtesting**.

---

##  Quick Setup

1. Place `.cs` file(s) in your cTrader Automate folder.
2. Tune parameters — distances, volume multipliers, profit targets, and risk limits.
3. Run in **Visual Backtest**, test!
4. Move to live trading when confident.

---




##  Disclaimer

Trading bots come with risk. Use them **with caution**, start with **dry runs**, and never risk more than you can afford to lose. You’re responsible for any trading outcomes.

---

### TL;DR

- **HTSBot** → fully automated trend strategy with protection logic.
- **HedgeBot** → smart insurance for your manual trades, using scalable hedging logic.


