using TMPro;
using UnityEngine;
using Gameplay.Leaderboard;

public class RoundUI : MonoBehaviour
{
    [SerializeField] private TMP_Text roundText;

    private void Update()
    {
        if (LeaderboardManager.Instance == null)
            return;

        var leaderboard = LeaderboardManager.Instance;

        switch (leaderboard.CurrentPhase)
        {
            case LeaderboardManager.RoundPhase.Fighting:
                roundText.text = $"ROUND {leaderboard.CurrentRound}";
                break;

            case LeaderboardManager.RoundPhase.BuildMode:
                roundText.text =
                    $"BUILD MODE\n" +
                    $"NEXT ROUND IN: {Mathf.CeilToInt(leaderboard.BuildTimer)}";
                break;

            case LeaderboardManager.RoundPhase.MatchOver:
                roundText.text = "MATCH OVER";
                break;
        }
    }
}