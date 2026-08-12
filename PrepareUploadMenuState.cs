using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Core.Collections.Scoped;
using Core.Dependency;
using Core.Localization;
using Crosstales;
using Crosstales.FB;
using Game.Core.Modding;
using Game.Modding;
using Game.Modding.Steam;
using JetBrains.Annotations;
using Menu.MainMenu.Mods;
using Shapez2UILib;
using ShapezShifter.Textures;
using Steamworks;
using Steamworks.Data;
using Steamworks.Ugc;
using UnityEngine;
using UnityEngine.Networking;
using Color = UnityEngine.Color;
using Image = UnityEngine.UI.Image;

namespace ModPublisher;

public class PrepareUploadMenuState : HUDMainMenuState
{
    private readonly Sprite _previewPlaceholder;

    [CanBeNull] private string _selectedPreview;
    private ResolvedMod? _currentMod;
    private string _initialDescription;
    private Item? _existingItem;
    private PublishedFileId? _uploadedItem;
    private int dialogId = 0;

    private GameObject[] _modInfoObjects;
    private GameObject[] _loadingObjects;
    private GameObject[] _uploadingPendingObjects;
    private GameObject[] _uploadingSuccessObjects;
    private GameObject[] _uploadingErrorObjects;
    private GameObject[] _allObjects;

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
        changelogLabel.Text = "menu.prepare-upload.mod-changelog".T();
        versionLabel.Text = "menu.prepare-upload.version-range".T();
        versionToLabel.Text = "menu.prepare-upload.version-to".T();
        versionToDropdown.Options = versionFromDropdown.Options =
            ModPublisher.GameBranches.Select(FormatBranch).ToList();
        uploadButton.Text = "menu.prepare-upload.upload-confirm".T();
        backButton.Text = "menu.prepare-upload.back".T();
        backButton.OnClick.AddListener(() =>
        {
            if(_uploadedItem == null)
                ShowObjects(_modInfoObjects); // There was an error, so show the mod info again
            else
                GoBack();
        });
        viewInWorkshopButton.Text = "menu.prepare-upload.view-workshop".T();
        viewInWorkshopButton.OnClick.AddListener(() =>
        {
            if(_uploadedItem != null)
                SteamFriends.OpenWebOverlay("https://steamcommunity.com/sharedfiles/filedetails/?id=" + _uploadedItem.Value);  
        });
        
        // Turns out Shapez's current version of Steamworks doesn't support versions at all 
        versionLabel.gameObject.SetActiveSelfExt(false);
        versionToLabel.gameObject.SetActiveSelfExt(false);
        versionToDropdown.gameObject.SetActiveSelfExt(false);
        versionFromDropdown.gameObject.SetActiveSelfExt(false);

        _loadingObjects = [networkAction.gameObject];
        _uploadingPendingObjects = [networkAction.gameObject];
        _uploadingSuccessObjects = [networkAction.gameObject, uploadStatusText.gameObject, backButton.gameObject, viewInWorkshopButton.gameObject];
        _uploadingErrorObjects = [networkAction.gameObject, uploadStatusText.gameObject, backButton.gameObject];
        _modInfoObjects =
        [
            infoText.gameObject, workshopStatusText.gameObject, titleText.gameObject, previewLabel.gameObject,
            openPreviewButton.gameObject, previewImage.gameObject, descriptionLabel.gameObject,
            descriptionInput.gameObject, changelogLabel.gameObject, changelogInput.gameObject,
            dependenciesToggle.gameObject, dependenciesLabel.gameObject, uploadButton.gameObject
        ];
        _allObjects = [
            networkAction.gameObject, infoText.gameObject, workshopStatusText.gameObject, titleText.gameObject, previewLabel.gameObject,
            uploadStatusText.gameObject, openPreviewButton.gameObject, previewImage.gameObject, descriptionLabel.gameObject,
            descriptionInput.gameObject, changelogLabel.gameObject, changelogInput.gameObject,
            dependenciesToggle.gameObject, dependenciesLabel.gameObject, uploadButton.gameObject,
            backButton.gameObject, viewInWorkshopButton.gameObject
        ]; 
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

    public override async void OnMenuEnterState(object payload)
    {
        try
        {
            if (payload is not ResolvedMod mod)
            {
                ModPublisher.Logger.Info?.Log("OnMenuEnterState payload was not resolved mod");
                GoBack();
                return;
            }

            _currentMod = mod;
            var currentDialogId = ++dialogId;
        
            networkAction.Text = "menu.prepare-upload.action.loading".T();
            ShowObjects(_loadingObjects);

            var query = Query.Items
                .LimitUser(SteamClient.SteamId);
            var page = 1;
            _existingItem = null;
            while (_existingItem == null)
            {
                var result = await query.GetPageAsync(page++);
                if (result == null || result.Value.ResultCount == 0)
                    break;
                foreach(var entry in result.Value.Entries)
                {
                    if (entry.Title == mod.Descriptor.ModTitle)
                    {
                        _existingItem = entry;
                        break;
                    }
                }
            }
            
            var sprite = _existingItem == null
                ? _previewPlaceholder
                : await DownloadPreview(_existingItem.Value.PreviewImageUrl) ?? _previewPlaceholder;

            if (currentDialogId != dialogId)
            {
                // The menu was reopened in the time it took to make the requests, don't do anything
                return;
            }

            titleText.Text = "menu.prepare-upload.mod-title".T().Bind("mod-title", new RawText(mod.Descriptor.ModTitle));

            if (HasLocalDependency(mod.Metadata.Dependencies))
            {
                workshopStatusText.Color = new Color(0.8039216f, 0.3607843f, 0.3607843f, 1f); //Color.indianRed;
                workshopStatusText.Text = "menu.prepare-upload.local-dependencies-warning".T();
            }
            else
            {
                workshopStatusText.Color = new Color(1f, 0.92f, 0.016f, 1f); //Color.yellowNice;
                if (_existingItem == null)
                    workshopStatusText.Text = "menu.prepare-upload.title-is-new".T();
                else
                    workshopStatusText.Text = "menu.prepare-upload.title-matched-existing".T();
            }

            previewImage.sprite = sprite;
            _selectedPreview = null;
            
            if (_existingItem == null)
                descriptionInput.Value = mod.Metadata.Description;
            else
                descriptionInput.Value = _existingItem.Value.Description;
            _initialDescription = _existingItem == null ? "" : _existingItem.Value.Description;
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
            
            ShowObjects(_modInfoObjects);
        }
        catch (Exception e)
        {
            ModPublisher.Logger.Error!.Log("Error loading prepare upload screen");
            ModPublisher.Logger.Error!.LogException(e);
        }
    }

    [ItemCanBeNull]
    private static async Task<Sprite> DownloadPreview(string url)
    {
        using var www = UnityWebRequestTexture.GetTexture(url);
        www.SendWebRequest();
        while(!www.isDone)
            await Task.Yield();

        if (www.result != UnityWebRequest.Result.Success)
        {
            ModPublisher.Logger.Error!.LogFormat("Error downloading preview image {0}: {1}", url, www.error);
            return null;
        }
        var texture = DownloadHandlerTexture.GetContent(www);
        RoundCorners(texture);
        var rect = new Rect(0, 0, texture.width, texture.height);
        return Sprite.Create(texture, rect, new Vector2(0.5f,0.5f));
    }

    private async void OnUploadPressed()
    {
        try
        {
            if (_currentMod == null)
                return;
            var mod = _currentMod.Value;
            ModPublisher.Logger.Info!.Log("Uploading Mod");
            networkAction.Text = "menu.prepare-upload.action.uploading".T().Bind("progress", new RawText(""));
            ShowObjects(_uploadingPendingObjects);
            
            var editor = _existingItem?.Edit()
                         ?? new Editor(WorkshopFileType.Community)
                             .WithTitle(mod.Descriptor.ModTitle)
                             .WithPrivateVisibility();
            editor.WithChangeLog(changelogInput.Value);
            
            if (!Directory.Exists(mod.Descriptor.DirectoryPath))
            {
                ShowUploadError("menu.prepare-upload.error.content-missing".T());
                ModPublisher.Logger.Error!.Log("Mod folder is no longer present");
                return;
            }
            editor.WithContent(mod.Descriptor.DirectoryPath);
            if (_selectedPreview != null)
            {
                ModPublisher.Logger.Info!.Log("Updating preview");
                if (!File.Exists(_selectedPreview))
                {
                    ShowUploadError("menu.prepare-upload.error.preview-missing".T());
                    ModPublisher.Logger.Error!.Log("Preview file is no longer present");
                    return;
                }
                editor.WithPreviewFile(_selectedPreview);
            }
            if (_initialDescription != descriptionInput.Value)
            {
                ModPublisher.Logger.Info!.Log("Updating description");
                editor.WithDescription(descriptionInput.Value);
            }

            var result = await editor.SubmitAsync(new Progress<float>(progress =>
            {
                networkAction.Text = "menu.prepare-upload.action.uploading".T().Bind("progress", new RawText((int)progress + "%"));
            }));
            if (!result.Success)
            {
                ShowUploadError("menu.prepare-upload.error.steam".T().Bind("error", new RawText(result.Result.ToString())));
                ModPublisher.Logger.Error!.Log("Error uploading mod");
                return;
            }
            var item = await Item.GetAsync(result.FileId);
            if (item == null)
            {
                ShowUploadError("menu.prepare-upload.error.item-missing".T());
                ModPublisher.Logger.Error!.Log("Unknown error uploading mod");
                return;
            }
            
            networkAction.Text = "menu.prepare-upload.action.uploading".T().Bind("progress", new RawText("100%"));
        
            if (dependenciesToggle.Value)
            {
                foreach (var dependency in mod.Metadata.Dependencies)
                {
                    if (dependency.ModId is not SteamModId steamId) continue;
                    ModPublisher.Logger.Info!.LogFormat("Adding dependency {0} ({1})", dependency.ModTitle, steamId.Id);
                    await item.Value.AddDependency(steamId.Id);
                }
            }

            _uploadedItem = result.FileId;
            uploadStatusText.Color = new Color(1f, 1f, 1f, 0.502f);
            uploadStatusText.Text = "menu.prepare-upload.success".T();
            ShowObjects(_uploadingSuccessObjects);
            ModPublisher.Logger.Info!.Log("Item was uploaded");
            
        }
        catch (Exception e)
        {
            ShowUploadError(new RawText(e.Message));
            ModPublisher.Logger.Error!.Log("Error preparing file upload");
            ModPublisher.Logger.Error!.LogException(e);
        }
    }
    
    public void ShowUploadError(IText error)
    {
        networkAction.Text = "menu.prepare-upload.action.error".T();
        uploadStatusText.Color = new Color(1f, 0.1f, 0.1f, 0.502f);
        uploadStatusText.Text = error;
        ShowObjects(_uploadingErrorObjects);
    }
    
    public void ExploreMods()
    {
        SteamFriends.OpenWebOverlay("https://steamcommunity.com/workshop/browse/?appid=2162800&browsesort=trend&section=readytouseitems");
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

    private bool HasLocalDependency(VersionedModReference[] dependencies)
    {
        return dependencies.Any(dependency => dependency.ModId is LocalModId);
    }

    private static void RoundCorners(Texture2D texture)
    {
        void ClearPixel(int x, int y, float alpha)
        {
            var original = texture.GetPixel(x, y);
            texture.SetPixel(x, y, new Color(original.r, original.g, original.b, original.a * alpha));
        }
        
        var radius = Mathf.Min(texture.width, texture.height) / 5;
        for (int y = 0; y <= radius; y++)
        {
            for (int x = 0; x <= radius; x++)
            {
                var dist = Mathf.Sqrt(x * x + y * y) - radius;
                if (dist <= 0)
                    continue;
                var alpha = Mathf.Clamp01(1 - dist / 3);
                ClearPixel(radius - x, radius - y, alpha);
                ClearPixel(texture.width - 1 - radius + x, radius - y, alpha);
                ClearPixel(texture.width - 1 - radius + x, texture.height - 1 - radius + y, alpha);
                ClearPixel(radius - x, texture.height - 1 - radius + y, alpha);
            }
        }
        texture.Apply();
    }

    private void ShowObjects(GameObject[] gameObjects)
    {
        foreach (var child in _allObjects)
        {
            child.SetActiveSelfExt(false);
        }
        foreach (var child in gameObjects)
        {
            child.SetActive(true);
        }
    }

    public HUDLocalizedText infoText;
    public HUDLocalizedText networkAction;
    public HUDLocalizedText titleText;
    public HUDLocalizedText workshopStatusText;
    public HUDLocalizedText uploadStatusText;
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
    public HUDButton backButton;
    public HUDButton viewInWorkshopButton;
}