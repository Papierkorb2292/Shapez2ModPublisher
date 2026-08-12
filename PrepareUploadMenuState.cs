using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Core.Collections.Scoped;
using Core.Dependency;
using Core.Localization;
using Crosstales.FB;
using Game.Core.Modding;
using Game.Modding;
using Game.Modding.Steam;
using JetBrains.Annotations;
using Menu.MainMenu.Mods;
using Shapez2UILib;
using ShapezShifter.Textures;
using Steamworks;
using Steamworks.Ugc;
using UnityEngine;
using UnityEngine.UI;

namespace ModPublisher;

public class PrepareUploadMenuState : HUDMainMenuState
{
    private readonly Sprite _previewPlaceholder;

    [CanBeNull] private string _selectedPreview;
    private ResolvedMod? _currentMod;
    private bool _descriptionWasChanged;

    public PrepareUploadMenuState()
    {
        var texture = FileTextureLoader.LoadTexture(ModPublisher.Resources.SubPath("preview_placeholder.png"));
        RoundCorners(texture);
        _previewPlaceholder = Sprite.Create(texture, new Rect(0.0f, 0.0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
    }
    
    [Construct]
    private void Construct()
    {
        uploadButton.OnClick.AddListener(OnUploadPressed);
        infoText.Text = "menu.prepare-upload.mod-info".T();
        previewLabel.Text = "menu.prepare-upload.mod-preview".T();
        openPreviewButton.OnClick.AddListener(() =>
        {
            Crosstales.Common.Util.Singleton<FileBrowser>.Instance.OnOpenFilesCompleted.AddListener(OnPreviewFileSelected);
            Crosstales.Common.Util.Singleton<FileBrowser>.Instance.OpenSingleFileAsync("Select preview image", GameEnvironmentManager.ModsPath, string.Empty, "png", "jpg");
        });
        previewImage.type = Image.Type.Simple;
        previewImage.preserveAspect = true;
        previewImage.material = null;
        openPreviewButton.Text = "menu.prepare-upload.open-preview".T();
        descriptionLabel.Text = "menu.prepare-upload.mod-description".T();
        descriptionInput.OnChange.AddListener(_ =>
        {
            _descriptionWasChanged = true;
        });
        changelogLabel.Text = "menu.prepare-upload.mod-changelog".T();
        versionLabel.Text = "menu.prepare-upload.version-range".T();
        versionToLabel.Text = "menu.prepare-upload.version-to".T();
        versionToDropdown.Options = versionFromDropdown.Options =
            ModPublisher.GameBranches.Select(FormatBranch).ToList();
        uploadButton.Text = "menu.prepare-upload.upload-confirm".T();
        
        // Turns out Shapez's current version of Steamworks doesn't support versions at all 
        versionLabel.gameObject.SetActiveSelfExt(false);
        versionToLabel.gameObject.SetActiveSelfExt(false);
        versionToDropdown.gameObject.SetActiveSelfExt(false);
        versionFromDropdown.gameObject.SetActiveSelfExt(false);
    }

    private static IText FormatBranch([CanBeNull] string branch)
    {
        return branch switch
        {
            null => new RawText("any"),
            "public" => new RawText("Latest Version"),
            _ => new RawText(branch)
        };
    }

    private void OnPreviewFileSelected(bool selected, string file, string files)
    {
        Crosstales.Common.Util.Singleton<FileBrowser>.Instance.OnOpenFilesCompleted.RemoveListener(OnPreviewFileSelected);
        if (!selected) return;
        try
        {
            var texture = FileTextureLoader.LoadTexture(file);
            RoundCorners(texture);
            previewImage.sprite = Sprite.Create(texture,
                new Rect(0.0f, 0.0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
            _selectedPreview = file;
        }
        catch (Exception e)
        {
            ModPublisher.Logger.Error!.Log("Error loading selected preview");
            ModPublisher.Logger.Error!.LogException(e);
        }
    }

    public override void OnDispose()
    {
            
    }
    public override void GoBack()
    {
        Menu.SwitchToState<HUDMenuModsState>();
    }

    public override void OnMenuEnterState(object payload)
    {
        if (payload is not ResolvedMod mod)
        {
            ModPublisher.Logger.Info?.Log("OnMenuEnterState payload was not resolved mod");
            GoBack();
            return;
        }

        _currentMod = mod;
        
        titleText.Text = "menu.prepare-upload.mod-title".T().Bind("mod-title", new RawText(mod.Descriptor.ModTitle));
        
        workshopStatusText.Color = Color.yellowNice;
        workshopStatusText.Text = "menu.prepare-upload.title-is-new".T();

        ModPublisher.Logger.Info!.LogFormat("Image: {0}", _previewPlaceholder);
        previewImage.sprite = _previewPlaceholder;
        _selectedPreview = null;

        _descriptionWasChanged = true; // TODO: When the description is fetched from the Workshop this should be false
        descriptionInput.Value = mod.Metadata.Description;
        descriptionInput.UIInputField.caretPosition = 0;
        descriptionInput.UIInputField.textComponent.rectTransform.anchoredPosition = Vector2.zero;
        descriptionInput.UIInputField.AssignPositioningIfNeeded();
        
        changelogInput.Value = "v" + mod.Metadata.Version;
        changelogInput.UIInputField.caretPosition = 0;
        changelogInput.UIInputField.textComponent.rectTransform.anchoredPosition = Vector2.zero;
        changelogInput.UIInputField.AssignPositioningIfNeeded();
        
        dependenciesLabel.Text = "menu.prepare-upload.toggle-dependencies".T().Bind("dependencies", FormatDependencies(mod.Metadata.Dependencies));
        dependenciesToggle.Value = true;

        versionToDropdown.Value = versionFromDropdown.Value = 0;
    }

    private async void OnUploadPressed()
    {
        try
        {
            if (_currentMod == null)
                return;
            var mod = _currentMod.Value;
            ModPublisher.Logger.Info!.Log("Uploading Mod");
            var editor = new Editor(WorkshopFileType.Community)
                .WithTitle(mod.Descriptor.ModTitle)
                .WithPrivateVisibility()
                .WithChangeLog(changelogInput.Value);
            if (!File.Exists(_selectedPreview))
            {
                ModPublisher.Logger.Error!.Log("Mod folder is no longer present"); //TODO: Notify user
                return;
            }
            editor.WithContent(mod.Descriptor.DirectoryPath);
            if (_selectedPreview != null)
            {
                ModPublisher.Logger.Info!.Log("Updating preview");
                if (!File.Exists(_selectedPreview))
                {
                    ModPublisher.Logger.Error!.Log("Preview file is no longer present"); //TODO: Notify user
                    return;
                }
                editor.WithPreviewFile(_selectedPreview);
            }
            if (_descriptionWasChanged)
            {
                ModPublisher.Logger.Info!.Log("Updating description");
                editor.WithDescription(descriptionInput.Value);
            }
        
            GoBack();

            var result = await editor.SubmitAsync();
            if (!result.Success)
            {
                ModPublisher.Logger.Error!.Log("Error uploading mod"); //TODO: Notify user
                return;
            }
            var item = await Item.GetAsync(result.FileId);
            if (item == null)
            {
                ModPublisher.Logger.Error!.Log("Unknown error uploading mod"); //TODO: Notify user
                return;
            }
            ModPublisher.Logger.Info!.Log("Item was uploaded");
        
            if (dependenciesToggle.Value)
            {
                foreach (var dependency in mod.Metadata.Dependencies)
                {
                    if (dependency.ModId is not SteamModId steamId) continue;
                    ModPublisher.Logger.Info!.LogFormat("Adding dependency {0} ({1})", dependency.ModTitle, steamId.Id);
                    await item.Value.AddDependency(steamId.Id);
                }
            }
        }
        catch (Exception e)
        {
            ModPublisher.Logger.Error!.Log("Error preparing file upload"); //TODO: Notify user
            ModPublisher.Logger.Error!.LogException(e);
        }
    }

    private IText FormatDependencies(VersionedModReference[] dependencies)
    {
        var steamDeps = dependencies.Where(dep => dep.ModId is SteamModId).ToList(); 
        var contents = ScopedList.Get<IText>();
        for(int i = 0; i < steamDeps.Count; i++)
        {
            if (i != 0)
                contents.Add(i == steamDeps.Count - 1 ? new RawText(" and ") : new RawText(", "));
            contents.Add(new RawText(steamDeps[i].ModTitle));
        }
        return new CombinedText(contents.ToArray());
    }

    private void RoundCorners(Texture2D texture)
    {
        void ClearPixel(int x, int y, float alpha)
        {
            var original = texture.GetPixel(x, y);
            texture.SetPixel(x, y, new Color(original.r, original.g, original.b, original.a * alpha));
        }
        
        var radius = Mathf.Min(texture.width, texture.height) / 5;
        ModPublisher.Logger.Info!.LogFormat("Radius: {0}", radius);
        for (int y = 0; y <= radius; y++)
        {
            for (int x = 0; x <= radius; x++)
            {
                var dist = Mathf.Sqrt(x * x + y * y) - radius;
                if (dist <= 0)
                    continue;
                var alpha = Mathf.Clamp01(1 - dist / 3);
                ModPublisher.Logger.Info!.LogFormat("Clearing: {0} {1}", radius - x, radius - y);
                ClearPixel(radius - x, radius - y, alpha);
                ClearPixel(texture.width - 1 - radius + x, radius - y, alpha);
                ClearPixel(texture.width - 1 - radius + x, texture.height - 1 - radius + y, alpha);
                ClearPixel(radius - x, texture.height - 1 - radius + y, alpha);
            }
        }
        texture.Apply();
    }


    public HUDLocalizedText infoText;
    public HUDLocalizedText titleText;
    public HUDLocalizedText workshopStatusText;
    public HUDLocalizedText previewLabel;
    public HUDButton openPreviewButton;
    public Image previewImage;
    public HUDLocalizedText descriptionLabel;
    public HUDInputField descriptionInput;
    public HUDLocalizedText changelogLabel;
    public HUDInputField changelogInput;
    public HUDLocalizedText dependenciesLabel;
    public HUDToggleControl dependenciesToggle;
    public HUDLocalizedText versionLabel;
    public HUDLocalizedText versionToLabel;
    public HUDDropdownControl versionFromDropdown;
    public HUDDropdownControl versionToDropdown;
    public HUDButton uploadButton;
}