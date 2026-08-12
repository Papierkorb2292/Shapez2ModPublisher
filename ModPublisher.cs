using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Core.Collections.Scoped;
using Core.Localization;
using Game.Core.Modding;
using Game.Modding;
using Game.Orchestration;
using JetBrains.Annotations;
using Menu.MainMenu.Mods;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Shapez2UILib;
using ShapezShifter.Kit;
using ShapezShifter.SharpDetour;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements.Collections;

namespace ModPublisher;

public class ModPublisher : IMod
{
    public static Core.Logging.ILogger Logger;
    // This list has to be hardcoded, because "someone" is still shipping a years old steamworks version with their game >:(
    [ItemCanBeNull]
    public static string[] GameBranches = [null, "public", "0.0.9-old", "0.1.1-old", "experimental", "modding_stable"];

    public static ModFolderLocator Resources;
    
    private static Hook _modMenuConstructorHook;
    
    public ModPublisher(Core.Logging.ILogger logger)
    {
        Logger = logger;
        Resources = ModDirectoryLocator.CreateLocator<ModPublisher>().SubLocator("Resources");
        MainMenuUIRegistrar.RegisterUI<PrepareUploadMenuState>(
            BuildPrepareUploadMenu,
            "ModUpload",
            "menu.prepare-upload.title".T(),
            "menu.prepare-upload.title",
            "there's no menu with this name, because I don't want this directly in the main menu",
            "About"
            );
        _modMenuConstructorHook = DetourHelper
            .CreatePostfixHook<HUDModMenuEntry, IModDescriptor, ModManifest, IDictionary<IModId, IModDescriptor>>(
                (entry, descriptor, modManifest, mods) => entry.Construct(descriptor, modManifest, mods),
                OnModEntryConstruct
            );
    }
    public void Dispose()
    {
        _modMenuConstructorHook.Dispose();
    }
    public void BuildPrepareUploadMenu(PrepareUploadMenuState prepareUploadMenuState)
    {
        var MainContent = prepareUploadMenuState.transform.GetChild(0);
        // Info Text
        var infoText = UIFactory.AddLocalizedTextPrimary(MainContent, prepareUploadMenuState);
        var infoTextRT = infoText.GetComponent<RectTransform>();
        infoTextRT.anchorMin = Vector2.up;
        infoTextRT.anchorMax = Vector2.up;
        infoTextRT.offsetMin = new Vector2(20, -80);
        infoTextRT.offsetMax = new Vector2(200, -20);
        // Network Action
        var networkAction = UIFactory.AddLocalizedTextPrimary(MainContent, prepareUploadMenuState);
        var networkActionRT = networkAction.GetComponent<RectTransform>();
        networkActionRT.anchorMin = Vector2.up;
        networkActionRT.anchorMax = Vector2.one;
        networkActionRT.offsetMin = new Vector2(20, -140);
        networkActionRT.offsetMax = new Vector2(-20, -20);
        networkAction.UIText.fontSize *= 3;
        networkAction.UIText.fontSizeMax *= 3;
        // Title
        var titleText = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var titleTextRT = titleText.GetComponent<RectTransform>();
        titleTextRT.anchorMin = Vector2.up;
        titleTextRT.anchorMax = Vector2.one;
        titleTextRT.offsetMin = new Vector2(20, -140);
        titleTextRT.offsetMax = new Vector2(-20, -80);
        titleText.Alignment = TextAlignmentOptions.Left;
        // Preview
        var previewLabel = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var previewLabelRT = previewLabel.GetComponent<RectTransform>();
        previewLabelRT.anchorMin = Vector2.up;
        previewLabelRT.anchorMax = Vector2.up;
        previewLabelRT.offsetMin = new Vector2(-20, -80);
        previewLabelRT.offsetMax = new Vector2(410, -20);
        previewLabel.Alignment = TextAlignmentOptions.Right;
        var openPreviewButton = UIFactory.AddButton(MainContent, prepareUploadMenuState, secondary: true);
        var openPreviewButtonRT = openPreviewButton.GetComponent<RectTransform>();
        openPreviewButtonRT.anchorMin = Vector2.up;
        openPreviewButtonRT.anchorMax = Vector2.up;
        openPreviewButtonRT.offsetMin = new Vector2(310, -120);
        openPreviewButtonRT.offsetMax = new Vector2(410, -80);
        var previewImage = UIFactory.AddSecondaryPanel(MainContent);
        var previewImageRT = previewImage.GetComponent<RectTransform>();
        previewImageRT.anchorMin = Vector2.up;
        previewImageRT.anchorMax = Vector2.up;
        previewImageRT.offsetMin = new Vector2(440, -280);
        previewImageRT.offsetMax = new Vector2(880, -30);
        // Workshop status
        var workshopStatusText = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var workshopStatusRT = workshopStatusText.GetComponent<RectTransform>();
        workshopStatusRT.anchorMin = Vector2.up;
        workshopStatusRT.anchorMax = Vector2.up;
        workshopStatusRT.offsetMin = new Vector2(20, -240);
        workshopStatusRT.offsetMax = new Vector2(400, -140);
        workshopStatusText.Alignment = TextAlignmentOptions.TopLeft;
        workshopStatusText.UIText.textWrappingMode = TextWrappingModes.Normal;
        // Upload status
        var uploadStatusText = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var uploadStatusRT = uploadStatusText.GetComponent<RectTransform>();
        uploadStatusRT.anchorMin = Vector2.up;
        uploadStatusRT.anchorMax = Vector2.one;
        uploadStatusRT.offsetMin = new Vector2(20, -240);
        uploadStatusRT.offsetMax = new Vector2(-20, -140);
        uploadStatusText.UIText.fontSize *= 2;
        uploadStatusText.UIText.fontSizeMax *= 2;
        uploadStatusText.UIText.textWrappingMode = TextWrappingModes.Normal;
        // Description
        var descriptionLabel = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var descriptionLabelRT = descriptionLabel.GetComponent<RectTransform>();
        descriptionLabelRT.anchorMin = Vector2.up;
        descriptionLabelRT.anchorMax = Vector2.one;
        descriptionLabelRT.offsetMin = new Vector2(20, -300);
        descriptionLabelRT.offsetMax = new Vector2(-20, -240);
        descriptionLabel.Alignment = TextAlignmentOptions.Left;
        var descriptionInput = UIFactory.AddInputField(MainContent, prepareUploadMenuState);
        var inputComponent = descriptionInput.UIInputField;
        inputComponent.lineType = TMP_InputField.LineType.MultiLineNewline;
        inputComponent.lineLimit = 100;
        inputComponent.characterLimit = 10000;
        inputComponent.characterValidation = TMP_InputField.CharacterValidation.None;
        inputComponent.textComponent.parseCtrlCharacters = false;
        inputComponent.textComponent.verticalAlignment = VerticalAlignmentOptions.Top;
        var descriptionInputRT = descriptionInput.GetComponent<RectTransform>();
        descriptionInputRT.anchorMin = Vector2.up;
        descriptionInputRT.anchorMax = Vector2.one;
        descriptionInputRT.offsetMin = new Vector2(20, -540);
        descriptionInputRT.offsetMax = new Vector2(-20, -300);
        // Changelog
        var changelogLabel = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var changelogLabelRT = changelogLabel.GetComponent<RectTransform>();
        changelogLabelRT.anchorMin = Vector2.up;
        changelogLabelRT.anchorMax = Vector2.one;
        changelogLabelRT.offsetMin = new Vector2(20, -610);
        changelogLabelRT.offsetMax = new Vector2(-20, -550);
        changelogLabel.Alignment = TextAlignmentOptions.Left;
        var changelogInput = UIFactory.AddInputField(MainContent, prepareUploadMenuState);
        var changelogComponent = changelogInput.UIInputField;
        changelogComponent.lineType = TMP_InputField.LineType.MultiLineNewline;
        changelogComponent.lineLimit = 100;
        changelogComponent.characterLimit = 10000;
        changelogComponent.characterValidation = TMP_InputField.CharacterValidation.None;
        changelogComponent.textComponent.parseCtrlCharacters = false;
        changelogComponent.textComponent.verticalAlignment = VerticalAlignmentOptions.Top;
        var changelogInputRT = changelogInput.GetComponent<RectTransform>();
        changelogInputRT.anchorMin = Vector2.up;
        changelogInputRT.anchorMax = Vector2.one;
        changelogInputRT.offsetMin = new Vector2(20, -750);
        changelogInputRT.offsetMax = new Vector2(-20, -610);
        // Dependencies
        var dependenciesLabel = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var dependenciesLabelRT = dependenciesLabel.GetComponent<RectTransform>();
        dependenciesLabelRT.anchorMin = Vector2.up;
        dependenciesLabelRT.anchorMax = Vector2.one;
        dependenciesLabelRT.offsetMin = new Vector2(90, -810);
        dependenciesLabelRT.offsetMax = new Vector2(-20, -750);
        dependenciesLabel.Alignment = TextAlignmentOptions.Left;
        var dependenciesToggle = UIFactory.AddToggle(MainContent, prepareUploadMenuState);
        var dependenciesToggleRT = dependenciesToggle.GetComponent<RectTransform>();
        dependenciesToggleRT.anchorMin = Vector2.up;
        dependenciesToggleRT.anchorMax = Vector2.up;
        dependenciesToggleRT.offsetMin = new Vector2(20, -810);
        dependenciesToggleRT.offsetMax = new Vector2(80, -750);
        // Version range
        var versionLabel = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var versionLabelRT = versionLabel.GetComponent<RectTransform>();
        versionLabelRT.anchorMin = Vector2.up;
        versionLabelRT.anchorMax = Vector2.one;
        versionLabelRT.offsetMin = new Vector2(20, -870);
        versionLabelRT.offsetMax = new Vector2(-20, -810);
        versionLabel.Alignment = TextAlignmentOptions.Left;
        var versionToLabel = UIFactory.AddLocalizedTextSecondary(MainContent, prepareUploadMenuState);
        var versionToLabelRT = versionToLabel.GetComponent<RectTransform>();
        versionToLabelRT.anchorMin = new Vector2(0.45f, 1);
        versionToLabelRT.anchorMax = new Vector2(0.45f, 1);
        versionToLabelRT.offsetMin = new Vector2(-50, -870);
        versionToLabelRT.offsetMax = new Vector2(50, -810);
        var versionFromDropdown = UIFactory.AddDropdown(MainContent, prepareUploadMenuState);
        var versionFromDropdownRT = versionFromDropdown.GetComponent<RectTransform>();
        versionFromDropdownRT.anchorMin = new Vector2(0.45f, 1);
        versionFromDropdownRT.anchorMax = new Vector2(0.45f, 1);
        versionFromDropdownRT.offsetMin = new Vector2(-180, -870);
        versionFromDropdownRT.offsetMax = new Vector2(-20, -810);
        var versionToDropdown = UIFactory.AddDropdown(MainContent, prepareUploadMenuState);
        var versionToDropdownRT = versionToDropdown.GetComponent<RectTransform>();
        versionToDropdownRT.anchorMin = new Vector2(0.45f, 1);
        versionToDropdownRT.anchorMax = new Vector2(0.45f, 1);
        versionToDropdownRT.offsetMin = new Vector2(20, -870);
        versionToDropdownRT.offsetMax = new Vector2(180, -810);
        // Upload Button
        var uploadButton = UIFactory.AddButton(MainContent, prepareUploadMenuState);
        var uploadButtonRT = uploadButton.GetComponent<RectTransform>();
        uploadButtonRT.anchorMin = Vector2.zero;
        uploadButtonRT.anchorMax = Vector2.zero;
        uploadButtonRT.offsetMin = new Vector2(20, 20);
        uploadButtonRT.offsetMax = new Vector2(420, 80);
        // Back Button (after upload)
        var backButton = UIFactory.AddButton(MainContent, prepareUploadMenuState);
        var backButtonRT = backButton.GetComponent<RectTransform>();
        backButtonRT.anchorMin = Vector2.up;
        backButtonRT.anchorMax = Vector2.up;
        backButtonRT.offsetMin = new Vector2(20, -300);
        backButtonRT.offsetMax = new Vector2(200, -240);
        // View In Workshop (after upload)
        var viewInWorkshopButton = UIFactory.AddButton(MainContent, prepareUploadMenuState);
        var viewInWorkshopButtonRT = viewInWorkshopButton.GetComponent<RectTransform>();
        viewInWorkshopButtonRT.anchorMin = Vector2.up;
        viewInWorkshopButtonRT.anchorMax = Vector2.up;
        viewInWorkshopButtonRT.offsetMin = new Vector2(250, -300);
        viewInWorkshopButtonRT.offsetMax = new Vector2(650, -240);
        
        
        prepareUploadMenuState.infoText = infoText;
        prepareUploadMenuState.networkAction = networkAction;
        prepareUploadMenuState.titleText = titleText;
        prepareUploadMenuState.previewLabel = previewLabel;
        prepareUploadMenuState.openPreviewButton = openPreviewButton;
        prepareUploadMenuState.previewImage = previewImage.transform.Find("Panel").GetComponent<Image>();
        prepareUploadMenuState.workshopStatusText = workshopStatusText;
        prepareUploadMenuState.uploadStatusText = uploadStatusText;
        prepareUploadMenuState.descriptionLabel = descriptionLabel;
        prepareUploadMenuState.descriptionInput = descriptionInput;
        prepareUploadMenuState.changelogLabel = changelogLabel;
        prepareUploadMenuState.changelogInput = changelogInput;
        prepareUploadMenuState.dependenciesLabel = dependenciesLabel;
        prepareUploadMenuState.dependenciesToggle = dependenciesToggle;
        prepareUploadMenuState.versionLabel = versionLabel;
        prepareUploadMenuState.versionToLabel = versionToLabel;
        prepareUploadMenuState.versionFromDropdown = versionFromDropdown;
        prepareUploadMenuState.versionToDropdown = versionToDropdown;
        prepareUploadMenuState.uploadButton = uploadButton;
        prepareUploadMenuState.backButton = backButton;
        prepareUploadMenuState.viewInWorkshopButton = viewInWorkshopButton;
    }
    public void OnModEntryConstruct(HUDModMenuEntry modEntry, IModDescriptor descriptor, ModManifest manifest, IDictionary<IModId, IModDescriptor> mods)
    {
        // Only allow local mods
        // This can't be done by checking the type because a mod can also be OverridenModDescriptor
        if (new DirectoryInfo(descriptor.DirectoryPath).Name != descriptor.ModTitle)
            return;
        var button = UIFactory.AddButton(modEntry.transform, modEntry, true);
        var buttonRT = button.GetComponent<RectTransform>();
        buttonRT.anchorMin = Vector2.zero;
        buttonRT.anchorMax = Vector2.zero;
        buttonRT.offsetMin = new Vector2(20, 10);
        buttonRT.offsetMax = new Vector2(350, 50);
        button.Text = "modpublisher.prepare-upload".T();
        button.OnClick.AddListener(() =>
        {
            (GameBootstrapper.GameOrchestrator.CurrentSubOrchestrator as MainMenuOrchestrator)
                ?.SwitchToState<PrepareUploadMenuState>(new ResolvedMod(manifest, descriptor));
        });
    }
}