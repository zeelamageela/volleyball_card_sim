namespace VolleyballCore
{
    /// <summary>
    /// Contract for the ability hooks Rally/Team call. Ported method-for-method
    /// from src/abilities.py's AbilityEngine -- the real AbilityEngine (ported
    /// from abilities.py itself, a much later milestone) will implement this.
    /// Every hook here is currently dormant in the sense that no concrete
    /// implementation exists yet in C#; Rally is written to call them exactly
    /// as the Python engine does, so a future AbilityEngine port slots in with
    /// no changes to Rally required.
    /// </summary>
    public interface IAbilityEngine
    {
        int HandSizeModifier();
        int ServeValueBonus();
        int AttackValueBonus(PlayerRole role, int attackCardValue);
        bool PierceBlock(PlayerRole role, int attackCardValue);
        int BlockValueBonus(PlayerRole role);
        int AdjacentBlockBonus(bool mbIsBlocking);
        void RecordDigSuccess(PlayerRole role, DigType digType);
        int ConsumeSetDelta();
        int OnSetBonus(int setCardValue = 0);
        void ActivateQuickSet();
        int ConsumeMbAttackBonus();
        int ChaseBonus();
        int DeflectDigThreshold();
        int TipValueBonus(PlayerRole role);
        int TipDigThreshold(PlayerRole role);
        bool SingleBlockOnly(PlayerRole role, int attackCardValue);
        int AttackDigThreshold(PlayerRole role);
        bool WipeBlock(PlayerRole role, int attackCardValue);
        bool RollShot(PlayerRole role, int attackCardValue);
        bool HeavySpin(PlayerRole role, int attackCardValue);
        bool SeamShot(PlayerRole role, int attackCardValue);
        bool SlideLanes(PlayerRole role, int attackCardValue);
        bool BackRowPierce(PlayerRole role, int attackCardValue);
        bool MinBlockerOnly(PlayerRole role, int attackCardValue);
        int WildBlockThreshold(PlayerRole role);
        int WideSpreadBonus(PlayerRole role);
        int ForceHighBlockThreshold(PlayerRole role, int attackCardValue);
        bool DeckSwapOpponentOnDig(DigType digType);
        void ActivateTipThreshold(int setCardValue);
        int ConsumeTipThresholdDelta();
        int DefenderDigThreshold(PlayerRole role);
        int SetterCoverThreshold();
        int DrawAndAddBlock(PlayerRole role);
        void SetHandSize(int n);
        int OverBlockBonus(PlayerRole role, int attackCardValue, bool isDoubleBlocked);
        bool HoldCardCheck(PlayerRole role, int attackCardValue);
        bool ExchangeCardEligible();
    }
}
