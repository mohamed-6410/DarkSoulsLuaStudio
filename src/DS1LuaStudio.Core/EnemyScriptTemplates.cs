using System.Globalization;
using System.Text.RegularExpressions;

namespace DS1LuaStudio.Core;

public sealed record EnemyScriptTemplate(string Id, string Name, string Description, string Source)
{
    public string Render(string goalName, int actNumber, int attackId, int followupId, double range, double phaseHp)
    {
        if (!LuaFunctionDrafts.IsIdentifier(goalName)) throw new ArgumentException("Use a Lua identifier for the AI name.");
        if (actNumber is < 1 or > 99 || attackId < 0 || followupId < 0 || !double.IsFinite(range) || range <= 0 || !double.IsFinite(phaseHp) || phaseHp <= 0 || phaseHp >= 1)
            throw new ArgumentException("Use Act 1–99, nonnegative attack IDs, a positive range, and a phase HP ratio between 0 and 1.");
        return Source.Replace("{{GoalName}}", goalName)
            .Replace("{{Act}}", actNumber.ToString("D2", CultureInfo.InvariantCulture))
            .Replace("{{AttackId}}", attackId.ToString(CultureInfo.InvariantCulture))
            .Replace("{{FollowupId}}", followupId.ToString(CultureInfo.InvariantCulture))
            .Replace("{{Range}}", range.ToString("0.###", CultureInfo.InvariantCulture))
            .Replace("{{PhaseHp}}", phaseHp.ToString("0.###", CultureInfo.InvariantCulture));
    }
}

/// <summary>Original starter patterns; attack IDs and goal registration must match the edited character.</summary>
public static class EnemyScriptTemplates
{
    public static IReadOnlyList<EnemyScriptTemplate> All { get; } = new[]
    {
        new EnemyScriptTemplate("attack", "Attack action", "An Act callback that queues one attack. Set the animation ID and reach for your character.", """
-- Register this action in your battle callback's action selection.
function {{GoalName}}_Act{{Act}}(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_Attack, 10, {{AttackId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    return 0
end
"""),
        new EnemyScriptTemplate("approach", "Approach then attack", "Close to the configured range, then queue the attack. Useful for melee enemies and gap closers.", """
function {{GoalName}}_Act{{Act}}(ai, goal, paramTbl)
    if ai:GetDist(TARGET_ENE_0) > {{Range}} then
        goal:AddSubGoal(GOAL_COMMON_ApproachTarget, 10, TARGET_ENE_0, {{Range}}, TARGET_SELF, false, -1)
    end
    goal:AddSubGoal(GOAL_COMMON_Attack, 10, {{AttackId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    return 0
end
"""),
        new EnemyScriptTemplate("combo", "Two-hit combo", "Queue a combo opener and final hit. Choose compatible animation IDs and adjust timing for your character.", """
function {{GoalName}}_Act{{Act}}(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_ComboAttack, 10, {{AttackId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    goal:AddSubGoal(GOAL_COMMON_ComboFinal, 10, {{FollowupId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    return 0
end
"""),
        new EnemyScriptTemplate("ranged", "Ranged spacing", "Back away when the target is too close, then attack. Choose a ranged animation supported by the character.", """
function {{GoalName}}_Act{{Act}}(ai, goal, paramTbl)
    if ai:GetDist(TARGET_ENE_0) < {{Range}} then
        goal:AddSubGoal(GOAL_COMMON_LeaveTarget, 3, TARGET_ENE_0, {{Range}}, TARGET_ENE_0, false, -1)
    end
    goal:AddSubGoal(GOAL_COMMON_Attack, 10, {{AttackId}}, TARGET_ENE_0, DIST_None, 0, 180)
    return 0
end
"""),
        new EnemyScriptTemplate("enemy", "Melee enemy starter", "Battle callbacks plus an Act01 action. Add a LUAINFO battle row named AI-name + Battle using the intended goal ID.", """
-- Register {{GoalName}}Battle in the matching .luainfo.
-- Add these globals to .luagnl when the archive uses a globals table.
function {{GoalName}}Battle_Activate(ai, goal)
    {{GoalName}}_Act01(ai, goal, nil)
end

function {{GoalName}}_Act01(ai, goal, paramTbl)
    if ai:GetDist(TARGET_ENE_0) > {{Range}} then
        goal:AddSubGoal(GOAL_COMMON_ApproachTarget, 10, TARGET_ENE_0, {{Range}}, TARGET_SELF, false, -1)
    end
    goal:AddSubGoal(GOAL_COMMON_Attack, 10, {{AttackId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    return 0
end

function {{GoalName}}Battle_Update(ai, goal)
    if goal:GetSubGoalNum() == 0 then
        return GOAL_RESULT_Success
    end
    return GOAL_RESULT_Continue
end

function {{GoalName}}Battle_Terminate(ai, goal)
end

function {{GoalName}}Battle_Interupt(ai, goal)
    return false
end
"""),
        new EnemyScriptTemplate("boss", "Boss with two HP phases", "Choose an opener above the HP threshold and a two-hit combo below it. The HP check changes future activations; it does not interrupt an attack already queued.", """
-- Register {{GoalName}}Battle in the matching .luainfo.
-- Add these globals to .luagnl when the archive uses a globals table.
function {{GoalName}}Battle_Activate(ai, goal)
    if ai:GetHpRate(TARGET_SELF) <= {{PhaseHp}} then
        {{GoalName}}_Act02(ai, goal, nil)
    else
        {{GoalName}}_Act01(ai, goal, nil)
    end
end

function {{GoalName}}_Act01(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_Attack, 10, {{AttackId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    return 0
end

function {{GoalName}}_Act02(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_ComboAttack, 10, {{AttackId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    goal:AddSubGoal(GOAL_COMMON_ComboFinal, 10, {{FollowupId}}, TARGET_ENE_0, {{Range}}, 0, 180)
    return 0
end

function {{GoalName}}Battle_Update(ai, goal)
    if goal:GetSubGoalNum() == 0 then
        return GOAL_RESULT_Success
    end
    return GOAL_RESULT_Continue
end

function {{GoalName}}Battle_Terminate(ai, goal)
end

function {{GoalName}}Battle_Interupt(ai, goal)
    return false
end
""")
    };
}
