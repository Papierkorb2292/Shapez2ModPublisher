# Shapez2 Mod Publisher
A Shapez2 mod that lets you upload mods to the Steam Workshop from within the game.

### Uploading a mod
To upload a mod using Mod Publisher, locate the mod in the mods menu and click "Prepare Upload To Workshop" to get to the upload menu. Optionally, you can add a preview image, change the description, and add a changelog. Once everything's configured, just click on "Upload Now" and wait for the upload to finish.

Unless turned off, Mod Publisher will also automatically add dependencies from your mod manifest to the workshop item.

### Updating existing mods

Of course, if you already have your mod on the workshop, Mod Publisher will update the existing item with the new content. For this to work, the title of your workshop item and the title of your local mod (the folder name inside `mods/`) has to be the same.

Note that Mod Publisher also lets you edit the description from the workshop, however the Steam API does not preserve the formatting from the workshop item. So if your description has formatting, it's still better to edit it through Steam. And as long as you don't make any changes to the description textbox of the mod, it shouldn't override the Steam description.