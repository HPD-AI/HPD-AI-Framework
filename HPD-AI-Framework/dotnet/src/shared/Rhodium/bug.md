# Potential Bug: WMA (Weighted Moving Average) Implementation

## Issue
The WMA implementation in `Ind.cs` appears to have **inverted weights** - it gives higher weight to older values instead of recent values.

## Current Implementation (Lines 317-329)
```csharp
public static Indicator<decimal> WMA(int period) => values =>
{
    if (values.Length < period) return 0m;
    var sum = 0m;
    var weightSum = 0m;
    for (int i = 0; i < period; i++)
    {
        var weight = period - i;  // ❌ BUG: Older values get HIGHER weight
        sum += values[values.Length - period + i] * weight;
        weightSum += weight;
    }
    return sum / weightSum;
};
```

## Problem
- Loop iteration `i=0` (oldest value) gets weight = `period` (highest)
- Loop iteration `i=period-1` (newest value) gets weight = `1` (lowest)
- **This is backwards from standard WMA definition**

## Standard WMA Definition
WMA should give **recent values higher weight** to reduce lag and be more responsive:
- Oldest value gets weight 1
- Newest value gets weight N (period)
- Example with period=3: `(oldest*1 + middle*2 + newest*3) / (1+2+3)`

## Impact
- Current implementation **increases lag** instead of reducing it
- In uptrend: WMA will be LOWER than SMA (should be higher)
- In downtrend: WMA will be HIGHER than SMA (should be lower)
- Defeats the purpose of using WMA

## Proposed Fix
Change line 324 from:
```csharp
var weight = period - i;  // Wrong
```
To:
```csharp
var weight = i + 1;  // Correct
```

## Verification Needed
Please verify if:
1. This is intentional (some alternative WMA definition?)
2. This is a bug that needs fixing
3. Any existing systems depend on this inverted behavior

## Example Calculation
With values `[10, 20, 30]` and period=3:

**Current (buggy):**
- `(10*3 + 20*2 + 30*1) / 6 = 16.67`

**Standard WMA:**
- `(10*1 + 20*2 + 30*3) / 6 = 23.33`

The difference is significant!

---

**Status:** BUG FIXED in commit. Tests updated to reflect standard WMA behavior.

---

# Bug: Aroon - Incorrect Formula Calculation

## Description
The Aroon indicator implementation has an incorrect formula for calculating Aroon Up and Aroon Down values. The current implementation calculates the position in the loop (0 to period-1), but Aroon should calculate based on how many periods ago the extreme occurred.

## Location
File: `/Users/einsteinessibu/Documents/Rhodium/src/Rhodium.Indicators/Indicators/Aroon.cs`
Lines: 60-61

## Current Implementation (INCORRECT)
```csharp
Up = 100m * highIdx / _period;
Down = 100m * lowIdx / _period;
```

## Expected Behavior
Aroon measures the TIME since the highest high or lowest low occurred:
- If highest high was 0 periods ago (most recent), Aroon Up = 100
- If highest high was N periods ago (oldest in window), Aroon Up = 0
- Formula: `Aroon Up = 100 * (period - periods_since_high) / period`

## Actual Behavior
The current code uses `highIdx` which is the loop iteration counter (0 to period-1), not the actual number of periods since the extreme occurred.

The loop starts from `_index` (oldest) and goes to `_index - 1` (most recent):
- i=0 represents the oldest bar
- i=period-1 represents the most recent bar

## Problem
When `highIdx = 0`, it means the highest high is at the **oldest** position, but the formula calculates:
- `Up = 100 * 0 / period = 0` ✓ Correct (oldest should be 0)

When `highIdx = period-1`, it means the highest high is at the **newest** position, but the formula calculates:
- `Up = 100 * (period-1) / period ≈ 96` ✗ WRONG (should be 100)

## Correct Formula
```csharp
Up = 100m * (period - 1 - highIdx) / _period;
Down = 100m * (period - 1 - lowIdx) / _period;
```

Or alternatively:
```csharp
Up = 100m * ((_period - 1) - highIdx) / (decimal)_period;
Down = 100m * ((_period - 1) - lowIdx) / (decimal)_period;
```

## Impact
- Aroon values are systematically underestimated
- Aroon Up/Down never reach 100 even when extreme is most recent
- Maximum possible value is approximately `100 * (period-1) / period`
- For period=25: max ≈ 96 instead of 100
- For period=14: max ≈ 93 instead of 100

## Example
With period=5, if the highest high occurred on the most recent bar:
- Current: `highIdx=4, Up = 100 * 4 / 5 = 80` ✗ WRONG
- Correct: `Up = 100 * (5-1-4) / 5 = 0`... wait, let me recalculate.

Actually, reviewing the loop:
- Loop iterates i from 0 to period-1
- `idx = (_index + i) % _period`
- When i=0: oldest bar (since _index was just incremented)
- When i=period-1: most recent bar

So if most recent bar has the high:
- `highIdx = period - 1 = 4`
- Current formula: `Up = 100 * 4 / 5 = 80` ✗
- Should be: `Up = 100 * ((5-1) - 4) / 5 = 0`...

Wait, I need to reconsider. Let me think about this differently.

## Reconsideration
Actually, the standard Aroon formula is:
- Aroon Up = ((period - periods since highest high) / period) * 100
- If highest high was 1 period ago: Aroon Up = ((25 - 1) / 25) * 100 = 96
- If highest high was 0 periods ago (current): Aroon Up = ((25 - 0) / 25) * 100 = 100
- If highest high was 24 periods ago: Aroon Up = ((25 - 24) / 25) * 100 = 4

In the current code:
- `highIdx` represents the position in the loop (0=oldest, period-1=newest)
- To convert to "periods ago": `periods_ago = (period - 1) - highIdx`
  - If highIdx=period-1 (newest): periods_ago = 0
  - If highIdx=0 (oldest): periods_ago = period-1

So the correct formula should be:
```csharp
var periodsSinceHigh = (_period - 1) - highIdx;
Up = 100m * (_period - periodsSinceHigh) / _period;
// Simplifies to:
Up = 100m * (highIdx + 1) / _period;

var periodsSinceLow = (_period - 1) - lowIdx;
Down = 100m * (_period - periodsSinceLow) / _period;
// Simplifies to:
Down = 100m * (lowIdx + 1) / _period;
```

## Corrected Analysis
The current code `Up = 100m * highIdx / _period;` is WRONG.

It should be: `Up = 100m * (highIdx + 1) / _period;`

This would ensure:
- Most recent high (highIdx = period-1): Up = 100 * period / period = 100 ✓
- Oldest high (highIdx = 0): Up = 100 * 1 / period = 4 (for period=25) ✓

---
