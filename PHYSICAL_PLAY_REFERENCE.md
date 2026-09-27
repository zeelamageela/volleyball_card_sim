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

## Combo: Two Hitters, One Lane

When both a front-row and a back-row attacker share the same lane, that's a **combo**. Decide your resolve order (which card goes first).

- Resolve the first card normally (see Attack Resolution below).
- If it **kills or deflects**, stop — the second card is discarded unused.
- If it's **genuinely stuffed**, remove the single **highest** blocker card in that lane, then resolve the second card against whatever block remains.
- Either card may independently be declared a tip.

### Example:
**You place:** Lane 2 front=8, Lane 2 back=8. **Dummy blocks lane 2:** [10, 8] (total 18).
- Resolve front card first: 8 vs 18 → **STUFFED**.
- Remove the highest blocker (10) → remaining block is just [8].
- Resolve back card: 8 vs 8 → **exact tie → DEFLECT** (see below).

---

## Attack Resolution

| Result | Condition | Outcome |
|--------|-----------|---------|
| **KILL** | Attack > Block | Defender must dig |
| **DEFLECT** | Attack **exactly equals** Block | Ball falls to your **own side** — you dig it |
| **STUFFED** | Attack < Block (any amount, not tied) | Defender scores immediately |

There is no more three-tier margin split and no more card-matching system — a lane's outcome is decided purely by comparing the attack value to the block total.

Deflect (exact tie) dig target:
- Target = the tied value itself (attack and block are equal).
- **No chase if this dig fails** — same as every other dig, it's an immediate point to the other side.
- If you dig it successfully, you keep attacking (the ball never crossed the net).

---

## Tip Rules

- Any attack card value **5 or less** may be declared a tip, front row only.
- Compare your tip to the lane's single **lowest** blocker card (not the block total) -- this is backwards from a normal hit vs. block:
  - Blocker's card is the **same or lower** than your tip → the blocker reads the soft shot and stuffs it outright. This includes an exact tie -- no deflection for a tip, unlike a tied hit.
  - Blocker's card is **higher** than your tip → the blocker's committed to a bigger swing and misses it. Tip beats the block: defender digs, **same-or-lower** against your tip's value. **No chase if this dig fails.**
- Back-row attacks cannot tip.

---

## Digging & Chase

- Any dig (kill or deflect) needs equal-or-higher.
- **A failed dig is always an immediate point to the attacking side — no chase, ever**, regardless of dig type (kill, tip, roll shot, tied-deflection) or block state. There's no "broken dig" free-ball recovery anymore.
- **Chase exists in exactly one place: a failed serve reception.** Add up to 2 more cards to your total, trying to reach the serve's value.
  - Chase succeeds → play continues normally (set + attack as usual), same as a clean pass.
  - Chase fails → ace, point to the server.

---

## Dummy Other Decisions

- **Attack lane:** First available lane
- **Tip/Hit:** Always tips if tip-eligible (card ≤5 on a front-row attack)
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
