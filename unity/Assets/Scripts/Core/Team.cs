using System;
using System.Collections.Generic;

namespace VolleyballCore
{
    /// <summary>Ported from src/players.py's Team class.</summary>
    public sealed class Team
    {
        public string Name { get; }
        public Deck Deck { get; }
        public List<Card> Hand { get; } = new();
        public Card? HeldCard { get; set; }
        public bool UseHand { get; }
        public string PassiveAbility { get; }
        public IReadOnlyDictionary<int, SetTemplate> SetterTemplates { get; }
        public IReadOnlyDictionary<int, SetTemplate> BrokenPlayTemplates { get; }
        public IAbilityEngine AbilityEngine { get; set; }
        public IReadOnlyList<GridPlayer> Players { get; }

        public Team(
            string name,
            Random rng,
            bool useHand = true,
            string deckType = "standard",
            string passiveAbility = null,
            IReadOnlyDictionary<int, SetTemplate> setterTemplates = null,
            IReadOnlyDictionary<int, SetTemplate> brokenPlayTemplates = null)
        {
            Name = name;
            Deck = new Deck(rng, deckType);
            UseHand = useHand;
            PassiveAbility = passiveAbility;
            SetterTemplates = setterTemplates ?? SetTemplate.Universal;
            BrokenPlayTemplates = brokenPlayTemplates ?? SetTemplate.BrokenPlay;
            Players = new[]
            {
                new GridPlayer(PlayerRole.Setter, 1),
                new GridPlayer(PlayerRole.Opp, 2),
                new GridPlayer(PlayerRole.Mb, 3),
                new GridPlayer(PlayerRole.Oh, 4),
                new GridPlayer(PlayerRole.Ds, 5),
                new GridPlayer(PlayerRole.Libero, 6),
            };
        }

        public GridPlayer GetPlayer(PlayerRole role)
        {
            foreach (var p in Players)
            {
                if (p.Role == role)
                {
                    return p;
                }
            }
            throw new ArgumentException($"No player with role {role}");
        }

        /// <summary>Back-row players who can receive a serve (excludes Setter).</summary>
        public List<GridPlayer> EligibleReceivers()
        {
            var result = new List<GridPlayer>();
            foreach (var p in Players)
            {
                if (p.CanReceiveServe() && p.IsBackRow())
                {
                    result.Add(p);
                }
            }
            return result;
        }

        public void DrawStartingHand()
        {
            if (!UseHand)
            {
                return;
            }
            int handSize = GameConstants.HandSize;
            if (PassiveAbility == "Deep Bench")
            {
                handSize += 1;
            }
            Hand.Clear();
            for (int i = 0; i < handSize; i++)
            {
                Hand.Add(Deck.Draw());
            }
        }

        /// <summary>Draw cards until hand is back to HandSize (plus any ability modifier).</summary>
        public void RefillHand()
        {
            if (!UseHand)
            {
                return;
            }
            if (HeldCard.HasValue)
            {
                Hand.Add(HeldCard.Value);
                HeldCard = null;
            }
            int maxSize = GameConstants.HandSize;
            if (PassiveAbility == "Deep Bench")
            {
                maxSize += 1;
            }
            if (AbilityEngine != null)
            {
                maxSize += AbilityEngine.HandSizeModifier();
            }
            while (Hand.Count < maxSize)
            {
                Hand.Add(Deck.Draw());
            }
        }

        /// <summary>Remove card from hand and send to discard.</summary>
        public void PlayCard(Card card)
        {
            Hand.Remove(card);
            Deck.Discard(card);
        }

        public void PlayCards(IEnumerable<Card> cards)
        {
            foreach (var c in cards)
            {
                PlayCard(c);
            }
        }

        /// <summary>Remove card from hand without discarding (HIT / BLOCK placement).</summary>
        public void CommitCard(Card card) => Hand.Remove(card);

        public void CommitCards(IEnumerable<Card> cards)
        {
            foreach (var c in cards)
            {
                CommitCard(c);
            }
        }

        /// <summary>Send a previously committed card to the discard pile.</summary>
        public void DiscardCard(Card card) => Deck.Discard(card);

        /// <summary>
        /// Draw a card for any action (serve/block/dig/attack).
        /// Passive ability: Elite Draw -- draw 2, keep highest.
        /// </summary>
        public Card DrawForAction()
        {
            if (PassiveAbility == "Elite Draw" && Deck.DrawPileSize >= 2)
            {
                Card card1 = Deck.Draw();
                Card card2 = Deck.Draw();
                if (card1.Value >= card2.Value)
                {
                    Deck.Discard(card2);
                    return card1;
                }
                Deck.Discard(card1);
                return card2;
            }
            return Deck.Draw();
        }

        public void DiscardMany(IEnumerable<Card> cards)
        {
            foreach (var c in cards)
            {
                DiscardCard(c);
            }
        }

        /// <summary>Draw the top card directly (used when hand is empty). Immediately discards it.</summary>
        public Card BlindDraw()
        {
            Card card = Deck.Draw();
            Deck.Discard(card);
            return card;
        }

        public override string ToString() => $"Team(\"{Name}\", hand=[{string.Join(", ", Hand)}])";
    }
}
