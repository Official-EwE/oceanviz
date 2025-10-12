# Video Creation

Oceanviz is designed with the ability to output video content to support efforts like media production. This means we give developer full control of the virtual camera and ensure reproducibility so the same scenario can be played back repeatedly without unexpected change in the behavior or distribution of the living things. (This was surprisingly hard to achieve in a game-like environment!)  

To create a repeatable video:

1. Add a new Camera in the location scene the shot will be in. In the Location object, assign the new camera in the OverrideCamera field.

2. With the new camera selected selected, in the Timeline panel, add a new Animation track. This will add proper components to the camera which is what we want - this way we can disable the whole animation by just disabling the camera object. Set the track to Record. You can easily set the positions of the camera by aligning it to the current view by pressing Ctrl-Shift-F with the Camera selected. Leave the first 5 second of the track empty so that the simulation has time to spin up.

3. With keframes added, you can convert them to an animation clip and edit in the Animation panel for more granular control. For compicated/realistic camera movement, create Cinemachine tracks instead.

4. For the entities to be present in the shot, prepare them in this format. It will have to placed in the InitialAPICalls method in SimulationAPI.cs:

```
// Switch location
SetLocation("Testing");
       
// Set view count
SetViewCount(2);

// Spawn dynamic preset
SpawnEntityGroup("Sea Bass", "Sea Bass 1");
SetEntityGroupPopulation("Sea Bass 1", 0.1f);
SetEntityGroupViewVisibility("Sea Bass 1", 0, 0.25f);
SetEntityGroupViewVisibility("Sea Bass 1", 1, 0.75f);

// // Spawn static preset
SpawnEntityGroup("Cystoseira", "Cystoseira 1");
SetEntityGroupPopulation("Cystoseira 1", 0.1f);
SetEntityGroupViewVisibility("Cystoseira 1", 0, 0.25f);
SetEntityGroupViewVisibility("Cystoseira 1", 1, 0.75f);


// Set turbidity for views
SetTurbidityForView(0, 0.33f);
SetTurbidityForView(1, 0.66f);
```


Record using the Recorder unity package. After the shot is done, unassign the camera from the OverrideCamera field, disable the camera and empty the InitialAPICalls method.
