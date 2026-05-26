# Physical Play Quick Reference

**For playing volleyball card sim with physical cards against a dummy opponent**

---

## Dummy Blocking Rules

### When Attacker Arms 1 Lane:
- Dummy places **all available cards** (up to 3) on that lane

### When Attacker Arms 2 Lanes:
1. **Count dummy's hand**: How many even cards? How many odd?
2. **Majority EVEN** → Double block **RIGHTMOST** lane (higher number)
3. **Majority ODD or TIE** → Double block **LEFTMOST** lane (lower number)

#### Examples:

**Example 1:**  
Attacker: Lanes 1 and 2  
Dummy hand: [2, 4, 7, 9, 10] → **3 even, 2 odd** → Even majority

**Block placement:**
- Lane 2: [2, 4] (double block on rightmost)
- Lane 1: [7] (single block on leftmost)

**Example 2:**  
Attacker: Lanes 1 and 3  
Dummy hand: [1, 3, 5, 6, 8] → **2 even, 3 odd** → Odd majority

**Block placement:**
- Lane 1: [1, 3] (double block on leftmost)
- Lane 3: [5] (single block on rightmost)

**Example 3:**  
Attacker: Lanes 2 and 3  
Dummy hand: [2, 4, 5, 7] → **2 even, 2 odd** → Tie

**Block placement:**
- Lane 2: [2, 4] (double block on leftmost — tie goes to odd rule)
- Lane 3: [5] (single block on rightmost)

### When Attacker Arms 3 Lanes:
- Dummy places **1 card per lane** (first 3 cards in hand)
- Lane 1: hand[0], Lane 2: hand[1], Lane 3: hand[2]

---

## Matching Rules

### Attacker-Blocker Match:
- When your attack card **equals** a block card value → **Lane eliminated**
- You **cannot choose** that lane
- Must pick from remaining lanes
- If **all lanes matched** → Dummy wins the rally

### Blocker-Blocker Match:
- When dummy places **two identical cards** in same lane → Cards cancel
- Lane becomes **unblocked** (0 block value)
- Your attack proceeds against empty block

### Example:
**You arm:** Lane 1=7, Lane 2=4, Lane 3=3  
**Dummy blocks:** Lane 1=7 (match!), Lane 2=9, Lane 3=2

**After matching:**
- Lane 1: **ELIMINATED** (7 vs 7 match)
- Lane 2: Available (4 vs 9 = stuffed)
- Lane 3: Available (3 vs 2+2 adjacent = 4, stuffed)

**Your choice:** Pick Lane 3 (loses by 1) or Lane 2 (loses by 5)

---

## Attack Resolution

| Result | Condition | Outcome |
|--------|-----------|---------|
| **KILL** | Attack > Block + 2 | Defender must dig |
| **DEFLECT** | Attack > Block (0-2 diff) | Defender must dig (easier) |
| **STUFFED** | Block ≥ Attack | Defender scores immediately |

---

## Dummy Other Decisions

- **Attack lane:** First available lane
- **Tip/Hit:** Always tips (if card ≤4)
- **Dig:** First card in hand
- **Chase:** First card in hand
- **Set:** First card in hand
- **Serve target:** Even card → first receiver, Odd card → last receiver

---

## Quick Parity Count

**Even cards:** 2, 4, 6, 8, 10  
**Odd cards:** 1, 3, 5, 7, 9

**Fast method:**  
- Separate hand into two piles (even/odd)
- Count which pile has more cards
- **More even** = rightmost gets double block
- **More odd or tie** = leftmost gets double block
