using TEngine;

namespace GameLogic
{
    public static class BattleNoticeEvents
    {
        public static readonly int Requested = RuntimeId.ToRuntimeId("BattleNoticeEvents.Requested");

        public static void Show(long battleId, string message)
        {
            GameEvent.Send(Requested, battleId, message);
        }
    }
}
