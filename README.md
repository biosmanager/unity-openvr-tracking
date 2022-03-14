# Unity OpenVR tracking without hassles

SteamVR tracking for Unity without requiring an HMD to be present or fiddling with the null driver. Useful if you just want to use tracked objects like Vive trackers. This is also not dependent on the SteamVR compositor framerate.

## Installation

1. Install [OpenVR for Unity XR plugin](https://github.com/ValveSoftware/unity-xr-plugin) in your project (the SteamVR Unity plugin is NOT required). Make sure to enable the plugin in the XR management settings or otherwise the `openvr_api.dll` will not be included in builds.
2. Add this package to the project.

## Configuration

Add `"requireHmd" : false` to the `steamvr` section of the SteamVR configuration file (`<Steam_Installation>/config/steamvr.vrsettings` or `<AppData>/Local/openvr/config/steamvr.vrsettings`). 

## Usage

1. Attach the `OVRT_Manager` script to a GameObject.
2. Attach either `OVRT_TrackedObject` or `OVRT_BoundTrackedObject` to a GameObject you want to track.

Bindings map the serial number of a tracked device to a string. Any bound tracked object with that string receives poses from the mapped tracked device. Bindings can be assigned by using the `Bindings` property of `OVRT_Manager`.

OVRT can also use tracker bindings from SteamVR. See `steamvr.vrsettings` for the used binding strings. For example, the right shoulder role is called `TrackerRole_RightShoulder`. When a `OVRT_BoundTrackedObject` has this binding string set, it receives poses from the tracker that was assigned to the right shoulder in SteamVR.

You can not open the SteamVR tracker role UI when no HMD is connected. In this case you can either edit `steamvr.vrsettings` or use this tool:
[SteamVR tracker role utility](https://github.com/biosmanager/steamvr-tracker-role-utility).

## Pose prediction

To predict poses into the future, one can adjust the `predictedSecondsToPhotonsFromNow` parameter of `OVRT_Manager`.