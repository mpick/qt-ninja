# qt-ninja

**SSMT (Sequential SMT) Divergence Indicators for NinjaTrader 8**

A comprehensive suite of indicators designed to detect Quarterly Theory SSMT divergences across multiple timeframes in the precious metals triad (Gold, Silver, Platinum). ; you can change the defaults to any comparison triads you choose depending on the asset(s) you wish to trade.

## 📊 Overview

These indicators identify SSMT divergences based on Quarterly Theory Priciples - Quarterly Theory created by Trader Daye - https://x.com/traderdaye

## 🎯 Indicators Included

### 1. SSMT Weekly Cycle
Compares **day-to-day** price action between instruments.
- **Timeframe**: Prior day (18:00 EST to 16:59 EST) vs current day
- **Best chart period**: 60-minute 
- **Use case**: Identifying daily divergences between GC, SI, and PL or your preferred Triad, Update the Settings when adding the indicator to your Chart

### 2. SSMT Monthly Cycle
Compares **week-to-week** price action between instruments.
- **Timeframe**: Prior week (Sunday 18:00 to Friday 16:59 EST) vs current week
- **Best chart period**: 240-minute/4hr
- **Use case**: Identifying weekly divergences for swing trading or to get in line with the Week's Bias

### 3. SSMT Daily Cycle
Compares **session-to-session** price action within each trading day.
- **Sessions**: 
  - Q1 (Asian): 18:00-23:59
  - Q2 (London): 00:00-05:59
  - Q3 (NY): 06:00-11:59
  - Q4 (PM): 12:00-16:59
- **Comparisons**: Q1→Q2, Q2→Q3, Q3→Q4, Q4→Q1
- **Best chart period**: 15-minute
- **Use case**: Intraday session sequential divergences

### 4. SSMT 90-Minute Cycle
Compares **90-minute cycle-to-cycle** price action within each 6-hour session.
- **Cycles**: Each session divided into four 90-minute quarters (16 cycles per day)
- **Comparisons**: Sequential cycles, of the Daily Sessions broken down into equal quarters
- **Best chart period**: 5-minute
- **Use case**: Precision timing for day trading

## 🚀 Installation

### Method 1: Direct File Copy (Recommended)
1. Download the indicator files from this repository
2. Navigate to: `Documents\NinjaTrader 8\bin\Custom\Indicators\`
3. Copy all `.cs` files into the Indicators folder
4. Open NinjaTrader 8
5. Press `F5` or go to **Tools → Compile** to compile the indicators
6. The indicators will now be available in your indicators list

### Method 2: Import via NinjaScript
1. Download the `.zip` export file (if provided)
2. In NinjaTrader 8, go to **Tools → Import → NinjaScript Add-On**
3. Select the downloaded `.zip` file
4. Click **Import**
5. Restart NinjaTrader 8

## ⚙️ Configuration

### Basic Settings
All indicators share these common settings:

- **Comparison Ticker**: First comparison instrument (e.g., "SI 12-25")
- **Comparison Ticker 2** *(Optional)*: Second comparison instrument (e.g., "PL 01-26")
  - Leave empty to compare only 2 assets
  - Fill in to enable triad comparison (3 assets)
- **Bearish SMT Color**: Color for bearish divergence lines (default: Red)
- **Bullish SMT Color**: Color for bullish divergence lines (default: Lime/Green)
- **Line Width**: Thickness of SMT trendlines (1-10, default: 2)

### How to Add to Chart
1. Open your chart (e.g., GC 12-25 on 60-minute)
2. Right-click chart → **Indicators**
3. Select the desired SMT indicator
4. Configure comparison tickers (SI 12-25, PL 01-26)
5. Adjust colors and line width as desired
6. Click **OK**

## 📖 How to Read the Indicators

### SMT Lines
- **Red/Bearish Lines**: One asset broke the prior high, the other(s) did not
- **Green/Bullish Lines**: One asset broke the prior low, the other(s) did not
- **Labels**: Show which comparison asset caused the divergence (e.g., "SI", "PL")
- **No Label**: The primary asset (chart instrument) caused the divergence

### Interpretation
- **Bearish SMT (Red)**: 
  - Comparison asset makes new high, primary doesn't
  - Suggests potential bearish reversal or weakness
- **Bullish SMT (Green)**:
  - Comparison asset makes new low, primary doesn't
  - Suggests potential bullish reversal or strength

### Example
If you're viewing GC (Gold) and:
- SI (Silver) breaks yesterday's high at 10:00 AM
- GC fails to break yesterday's high at the same time
- **Result**: Red line drawn from yesterday's GC high to current candle, labeled "SI"

## 💡 Usage Tips

1. **Timeframe Selection**:
   - Daily: Use 60m charts for clarity
   - Weekly: Use 240m charts or daily
   - Session: Use 15m charts
   - 90m Cycle: Use 5m charts

2. **Data Loading**:
   - Increase "Days to load" in chart properties (30-90 days recommended)
   - This allows scrolling back to see historical divergences

3. **Multiple Timeframes**:
   - Run different SMT indicators on different timeframes simultaneously
   - Higher timeframe divergences often more significant

4. **Triad Analysis**:
   - Enable all three metals (GC, SI, PL) for comprehensive analysis; you can use Copper HG instead of Platinum PL
   - Other Common Triads are these
   - The Index Triad; ES/NQ/YM (alternative you can use NKD instead of YM)
   - The Fuels Triad; CL/RB/HO
   - The Currencies Triad; 6E/6B/6S (DXY inversion SMT currently is not capable, suggested to use Swiss Franc in place of DXY)
   - The Bonds Triad; UB/TN/ZF
   - The Crypto Triad; BTC/ETH/SOL
   - Look for confluence when multiple divergences align


## 🧠 Strategies Included

### 1. LevelBreakRecover.cs
Sets a Limit Order at a desired level once a User Inputted quantity of bar closures occur below/above the desired level, and then a "recovery" takes place where price makes bar closures below/above the desired level in the quantity of bar closures set by the user. 

### 2. ReentryTest.cs
Sets a Limit Order at a desired level and will fill once price hits the level. TP & SL settings given by the user, if the TP is hit, a duplicate order will be re-created at the original level waiting to be filled again.  


## 🛠️ Technical Details

### Requirements
- **Platform**: NinjaTrader 8
- **Markets**: Works with any correlated instruments (futures, forex, stocks)
- **Data**: Real-time or historical data supported

### How It Works
1. Tracks prior period's high and low (day/week/session/cycle)
2. Monitors when each asset breaks those levels
3. Detects divergences when timing differs between assets
4. Draws lines from prior high/low to divergence point
5. Labels the asset that led the break

### Trading Day Definition
- **Start**: Sunday 18:00 EST
- **End**: Friday 16:59 EST
- **Close**: Friday 17:00-17:59 EST (market closed hour)

## 📝 Version History

### v1.0.0 (Initial Release)
- SSMT Monthly Cycle
- SSMT Weekly Cycle
- SSMT Daily Cycle
- SSMT 90-Minute Cycle
- Support for 2 or 3 asset comparison; (leave the 3rd Asset blank in settings if you don't wish to compare to a 3rd)
- Customizable colors and line styles
- Automatic timeframe adaptation

## 🤝 Contributing

Contributions are welcome! Feel free to:
- Report bugs via Issues
- Suggest enhancements
- Submit pull requests with improvements

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## ⚠️ Disclaimer

These indicators are for educational and informational purposes only. They do not constitute financial advice. Trading futures and derivatives involves substantial risk of loss. Past performance is not indicative of future results. Always perform your own analysis and consult with a licensed financial advisor before making trading decisions.

## 🙏 Acknowledgments

- Built for NinjaTrader 8 platform
- Inspired by Smart Money Technique concepts
- Designed for precious metals triad analysis (GC, SI, PL)

## 📧 Support

For questions, issues, or suggestions, please open an issue on GitHub. Reach GGxTrades on X if you have any questions or need special guidance https://x.com/GGxTrades

---

**Happy Trading! 📈**