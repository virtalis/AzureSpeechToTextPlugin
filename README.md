# Azure Speech to Text VisRen Plugin
This is a plugin that exposes the Azure Speech to Text capability to the VisRen Lua environment so scripts in a scene can perform speech to text operations and receive the spoken text as a string.

## Compile
Visual Studio 2019 recommended.

1. Clone this repository (or download the zip)
2. Open the AzureSpeechToText solution
3. Edit Exports.cs as follows:
 a. Replace the placeholder values for the Azure subscription keys
 `var config = SpeechConfig.FromSubscription("YOUR_SUBSCRIPTION_KEY", "YOUR_SUBSCRIPTION_REGION");`
 b. Replace the placeholder VisRen native SDK key with a valid license
 `return "YOUR_VISREN_NATIVE_API_KEY";`
4. Build the project

## Install
1. Copy the bin/Release (or bin/Debug if you build debug mode) folder to Documents/VisionaryRender/plugins and rename it to "AzureSpeechToText". (if your VisRen plugins folder is elsewhere, adapt as necessary)
2. Create a `plugin.txt` file in the AzureSpeechToText directory, and inside it enter `AzureSpeechToText.dll`

## Usage

### Verify successful installation
1. Press F6 (or File -> Windows -> Settings)
2. Select the "Plugins" category
3. Ensure the "AzureSpeechToText" plugin row is present and its status says "Loaded"

### Capture voice input as text
To start listening and receive spoken input as text, you need to register an "onSpeech" callback.
You can do this in the script Console, or in a Create script or somewhere that will only execute once
```lua
__registerCallback("onSpeech", function(text)
  print("Spoken: ", text)
end)
```

Now that the callback is registered, you can freely call the following functions from any other script (e.g. button press, gui click, etc):

To start listening
```lua
AzureSpeechToText.StartListening()
```

To stop listening
```lua
AzureSpeechToText.StopListening()
```

### Other callbacks
If you want notification of other events you can also subscribe to the following events using the same `__registerCallback` method as above:
* `onSpeechSessionStarted`
* `onSpeechSessionStopped`
* `onSpeechStartDetected`
* `onSpeechEndDetected`


## License
MIT