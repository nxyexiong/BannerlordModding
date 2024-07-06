using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Party;

namespace HenrysMod
{
    public class HenrysSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            // main menu button
            Module.CurrentModule.AddInitialStateOption(
                new InitialStateOption("HenrysModButton",
                    new TextObject("Henry's Mod", null), 9990,
                    () => { InformationManager.DisplayMessage(new InformationMessage("Henry's mod is running!")); },
                    () => { return (false, null); }));

            // setup patches
            var harmony = new Harmony("HenrysMod");
            harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
        }

        protected override void InitializeGameStarter(Game game, IGameStarter starterObject)
        {
            if (starterObject is CampaignGameStarter)
            {
                var starter = starterObject as CampaignGameStarter;
                starter.AddBehavior(new HenrysCampaignBehavior());
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            gameStarterObject.AddModel(new HenrysCustomBattleAgentStatCalculateModel());
        }
    }

    public class HenrysCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, new Action(DailyTick));
        }

        public override void SyncData(IDataStore dataStore) { }

        public void DailyTick()
        {
            foreach (var banditParty in Campaign.Current.BanditParties)
            {
                var member = banditParty.MemberRoster;
                if (member == null) continue;
                for (var idx = 0; idx < member.Count; idx++)
                {
                    var character = member.GetCharacterAtIndex(idx);
                    var number = member.GetElementNumber(idx);
                    if (character == null || character.IsHero) continue;
                    var rate = MathF.Pow(MathF.E, -0.08f * number); // e^(-0.08x)
                    var add = (int)(number * rate);
                    if (add <= 0) continue;
                    member.AddToCountsAtIndex(idx, add);
                }
            }
        }
    }

    public class HenrysCustomBattleAgentStatCalculateModel : CustomBattleAgentStatCalculateModel
    {
        public override void UpdateAgentStats(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            base.UpdateAgentStats(agent, agentDrivenProperties);

            if (agent == null || agentDrivenProperties == null)
                return;

            // attack speed
            if (agent.IsHuman)
            {
                var weapon = agent.WieldedWeapon.CurrentUsageItem;
                var skill = 0f;
                if (weapon != null)
                {
                    skill = MissionGameModels.Current.AgentStatCalculateModel
                        .GetEffectiveSkillForWeapon(agent, weapon);
                    if (skill < 0) skill = 0;
                }
                var rate = 1f + skill * skill * 0.00001f; // 1 + 0.00001x^2
                agentDrivenProperties.SwingSpeedMultiplier *= rate;
                agentDrivenProperties.ThrustOrRangedReadySpeedMultiplier *= rate;
                agentDrivenProperties.ReloadSpeed *= rate;
            }

            // movement speed
            if (agent.IsHuman)
            {
                var skill = MissionGameModels.Current.AgentStatCalculateModel
                    .GetEffectiveSkill(agent, DefaultSkills.Scouting);
                if (skill < 0) skill = 0;
                var rate = 1f + skill * skill * 0.000007f; // 1 + 0.000007x^2
                agentDrivenProperties.MaxSpeedMultiplier *= rate;
            }
            else if (agent.IsMount)
            {
                var rider = agent.RiderAgent;
                var skill = 0f;
                if (rider != null)
                {
                    skill = MissionGameModels.Current.AgentStatCalculateModel
                        .GetEffectiveSkill(rider, DefaultSkills.Scouting);
                    if (skill < 0) skill = 0;
                }
                var rate = 1f + skill * skill * 0.000015f; // 1 + 0.000015x^2
                agentDrivenProperties.MountSpeed *= rate;
                agentDrivenProperties.MountDashAccelerationMultiplier *= rate;
                agentDrivenProperties.MountManeuver *= rate;
            }
        }
    }

    [HarmonyPatch(typeof(MobileParty), "CalculateSpeed")]
    public class MobileParty_CalculateSpeed_Patch
    {
        public static void Postfix(ref MobileParty __instance, ref float __result)
        {
            HandlePartySpeed(ref __instance, ref __result);
        }

        private static void HandlePartySpeed(ref MobileParty party, ref float speed)
        {
            if (party.LeaderHero == null ||
                party.DefaultBehavior == AiBehavior.EscortParty)
                return;

            var skill = party.LeaderHero.GetSkillValue(DefaultSkills.Scouting);
            if (skill < 0) skill = 0;
            var rate = 1f + skill * skill * 0.00001f; // 1 + 0.00001x^2
            speed *= rate;
        }
    }

    [HarmonyPatch(typeof(Mission), "DecideWeaponCollisionReaction")]
    public class Mission_DecideWeaponCollisionReaction_Patch
    {
        public static void Postfix(ref Blow registeredBlow, ref AttackCollisionData collisionData,
            ref Agent attacker, ref Agent defender, ref MissionWeapon attackerWeapon,
            ref bool isFatalHit, ref bool isShruggedOff, ref MeleeCollisionReaction colReaction)
        {
            HandleCutThrough(ref attacker, ref collisionData, ref attackerWeapon, ref colReaction);
        }

        private static void HandleCutThrough(ref Agent attacker, ref AttackCollisionData collisionData,
            ref MissionWeapon attackerWeapon, ref MeleeCollisionReaction colReaction)
        {
            if (attacker == null || attacker.Character == null ||
                attackerWeapon.CurrentUsageItem == null)
                return;

            if (colReaction == MeleeCollisionReaction.SlicedThrough)
                return;

            if (collisionData.CollisionResult != CombatCollisionResult.StrikeAgent)
                return;

            var skill = (float)MissionGameModels.Current.AgentStatCalculateModel
                .GetEffectiveSkillForWeapon(attacker, attackerWeapon.CurrentUsageItem);
            if (skill < 0) skill = 0;

            var rate = MathF.Log(0.03f * skill) / 2.0f; // ln(0.03x) / 2
            if (rate <= 0.0f) rate = 0.0f;
            else if (rate >= 1.0f) rate = 1.0f;
            if (Utils.Random.NextFloat() > rate)
                return;

            colReaction = MeleeCollisionReaction.SlicedThrough;

            if (attacker.IsPlayerControlled)
                InformationManager.DisplayMessage(new InformationMessage("Power through attack!"));
        }
    }

    [HarmonyPatch(typeof(Mission), "RegisterBlow")]
    public class Mission_RegisterBlow_Patch
    {
        public static void Prefix(ref Agent attacker, ref Agent victim, ref GameEntity realHitEntity,
            ref Blow b, ref AttackCollisionData collisionData, ref MissionWeapon attackerWeapon,
            ref CombatLogData combatLogData)
        {
            HandleCriticalHit(ref attacker, ref b, ref attackerWeapon, ref collisionData);
            HandleDamageReduction(ref victim, ref b, ref collisionData);
        }

        public static void Postfix(ref Agent attacker, ref Agent victim, ref GameEntity realHitEntity,
            ref Blow b, ref AttackCollisionData collisionData, ref MissionWeapon attackerWeapon,
            ref CombatLogData combatLogData)
        {
            HandleLifeSteal(ref attacker, ref b, ref collisionData);
        }

        private static void HandleCriticalHit(ref Agent attacker, ref Blow b,
            ref MissionWeapon attackerWeapon, ref AttackCollisionData collisionData)
        {
            if (attacker == null || attacker.Character == null ||
                attackerWeapon.CurrentUsageItem == null)
                return;

            if (collisionData.CollisionResult != CombatCollisionResult.StrikeAgent)
                return;

            var skill = (float)MissionGameModels.Current.AgentStatCalculateModel
                .GetEffectiveSkillForWeapon(attacker, attackerWeapon.CurrentUsageItem);
            if (skill < 0) skill = 0;

            var rate = skill * skill * 0.00001f; // 0.00001x^2
            if (rate <= 0.0f) rate = 0.0f;
            else if (rate >= 1.0f) rate = 1.0f;
            if (Utils.Random.NextFloat() > rate)
                return;

            b.InflictedDamage *= 2;

            if (attacker.IsPlayerControlled)
                InformationManager.DisplayMessage(
                    new InformationMessage("Critical hit!", Color.FromUint(0xFF0000)));
        }

        private static void HandleDamageReduction(ref Agent victim, ref Blow b,
            ref AttackCollisionData collisionData)
        {
            if (victim == null || victim.Character == null)
                return;

            if (collisionData.CollisionResult != CombatCollisionResult.StrikeAgent)
                return;

            var skill = (float)MissionGameModels.Current.AgentStatCalculateModel
                .GetEffectiveSkill(victim, DefaultSkills.Athletics);
            if (skill < 0) skill = 0;

            var rate = 1.0f - 0.0016f * skill; // 1 - 0.0016x
            if (rate <= 0.0f) rate = 0.0f;
            else if (rate >= 1.0f) rate = 1.0f;

            b.InflictedDamage = (int)(b.InflictedDamage * rate);
        }

        private static void HandleLifeSteal(ref Agent attacker, ref Blow b,
            ref AttackCollisionData collisionData)
        {
            if (attacker == null || attacker.Character == null)
                return;

            if (collisionData.CollisionResult != CombatCollisionResult.StrikeAgent ||
                b.InflictedDamage <= 0)
                return;

            var skill = (float)MissionGameModels.Current.AgentStatCalculateModel
                .GetEffectiveSkill(attacker, DefaultSkills.Medicine);
            if (skill < 0) skill = 0;

            var rate = 0.0012f * skill; // 0.0012x
            if (rate <= 0.0f) rate = 0.0f;
            else if (rate >= 1.0f) rate = 1.0f;

            attacker.Health += b.InflictedDamage * rate;
            if (attacker.Health > attacker.HealthLimit)
                attacker.Health = attacker.HealthLimit;
        }
    }

    public class Utils
    {
        public static System.Random Random = new System.Random();
    }

}
