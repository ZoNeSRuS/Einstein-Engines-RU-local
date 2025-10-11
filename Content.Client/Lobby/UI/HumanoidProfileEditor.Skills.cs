using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.AWS.Skills;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Utility;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private readonly Dictionary<ProtoId<SkillPrototype>, OptionButton> _skillSelectors = new();
    private readonly Dictionary<ProtoId<SkillPrototype>, List<Enum>> _skillLevelOptions = new();
    private bool _suppressSkillEvents;

    private void InitializeSkillsTab()
    {
        BuildSkillsList();
        UpdateSkillsTab();
    }

    private void BuildSkillsList()
    {
        _skillSelectors.Clear();
        _skillLevelOptions.Clear();
        if (SkillsList.ContentContainer != null)
            foreach (var child in SkillsList.ContentContainer.Children.ToArray())
                child.Dispose();

        var skillPrototypes = _prototypeManager.EnumeratePrototypes<SkillPrototype>()
            .OrderBy(skill => skill.Category)
            .ThenBy(skill => skill.ID)
            .ToList();

        if (skillPrototypes.Count == 0)
            return;

        foreach (var categoryGrouping in skillPrototypes.GroupBy(skill => skill.Category))
        {
            var categoryId = categoryGrouping.Key;
            var categoryName = Loc.TryGetString($"skills-category-{categoryId}", out var localizedCategory)
                ? localizedCategory
                : categoryId.ToString();

            var categoryBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                Margin = new Thickness(0, 0, 0, 8),
            };

            categoryBox.AddChild(new Label
            {
                Text = categoryName,
                StyleClasses = { "LabelHeadingBigger" },
            });

            foreach (var skill in categoryGrouping)
            {
                var row = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    HorizontalExpand = true,
                    Margin = new Thickness(0, 2, 0, 2),
                };

                var skillName = Loc.TryGetString($"skills-skillname-{skill.ID}", out var localizedSkill)
                    ? localizedSkill
                    : skill.ID.ToString();

                row.AddChild(new Label
                {
                    Text = skillName,
                    HorizontalExpand = true,
                    VerticalAlignment = VAlignment.Center,
                });

                var selector = new OptionButton
                {
                    HorizontalAlignment = HAlignment.Right,
                    MinWidth = 150,
                };

                foreach (SkillLevel level in Enum.GetValues(typeof(SkillLevel)))
                {
                    var levelKey = $"skills-level-{level.ToString().ToLowerInvariant()}";
                    var levelName = Loc.TryGetString(levelKey, out var localizedLevel)
                        ? localizedLevel
                        : level.ToString();
                    selector.AddItem(levelName, (int) level);
                }

                selector.OnItemSelected += args =>
                {
                    selector.SelectId(args.Id);
                    if (_suppressSkillEvents)
                        return;

                    OnSkillLevelSelected(skill.ID, (SkillLevel) args.Id);
                };

                _skillSelectors[skill.ID] = selector;
                _skillLevelOptions[skill.ID] = new List<Enum>(skill.Cost.Keys);

                row.AddChild(selector);
                categoryBox.AddChild(row);
            }

            SkillsList.AddChild(categoryBox);
        }
    }

    private void UpdateSkillsTab()
    {
        if (_skillSelectors.Count == 0)
            BuildSkillsList();

        UpdateSkillSelectors(Profile);
        UpdateSkillPointsView();
    }

    private void UpdateSkillSelectors(HumanoidCharacterProfile? profile)
    {
        _suppressSkillEvents = true;

        foreach (var (skillId, selector) in _skillSelectors)
        {
            var level = SkillLevel.NonSkilled;
            if (profile?.SkillPreferences?.Skills != null &&
                profile.SkillPreferences.Skills.TryGetValue(skillId, out var storedLevel))
            {
                level = storedLevel is SkillLevel typed
                    ? typed
                    : (SkillLevel) storedLevel.GetHashCode();
            }

            selector.SelectId((int) level);
        }

        _suppressSkillEvents = false;
    }

    private void UpdateSkillPointsView()
    {
        if (Profile == null)
        {
            SkillPointsLabel.Text = Loc.GetString("humanoid-profile-editor-skills-no-profile");
            SkillPointsBar.Visible = false;
            return;
        }

        SkillPointsBar.Visible = true;
        var controller = BuildSkillPointController(Profile);

        var current = controller.CurrentPoints;
        var max = controller.MaxPoints;

        SkillPointsBar.MaxValue = Math.Max(max, 1);
        SkillPointsBar.Value = Math.Clamp(current, 0, SkillPointsBar.MaxValue);
        SkillPointsLabel.Text = Loc.GetString(
            "humanoid-profile-editor-skills-points-label",
            ("available", current),
            ("total", max));
    }

    private SkillPointController BuildSkillPointController(HumanoidCharacterProfile profile)
    {
        var maxPoints = GetSkillPointPool(profile);
        var container = profile.SkillPreferences?.Clone() ?? new SkillContainer();
        return new SkillPointController(maxPoints, _skillLevelOptions, _prototypeManager, container);
    }

    private int GetSkillPointPool(HumanoidCharacterProfile profile)
    {
        var points = profile.SkillPreferences?.AdditionalSkillPoints ?? 0;

        foreach (var proto in _prototypeManager.EnumeratePrototypes<AgeSkillPointsPrototype>())
        {
            if (proto.Specie != profile.Species)
                continue;

            if (profile.Age < proto.MinAge)
                break;

            foreach (var (threshold, amount) in proto.PointsForAges.OrderBy(p => p.Key))
            {
                if (profile.Age >= threshold)
                    points += amount;
            }

            break;
        }

        return points;
    }

    private void OnSkillLevelSelected(ProtoId<SkillPrototype> skillId, SkillLevel newLevel)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithSkillLevel(skillId, newLevel);
        IsDirty = true;
        UpdateSkillPointsView();
        UpdateSaveButton();
    }
}
