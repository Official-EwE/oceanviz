# Location
### Adding a new Location to Unity

1. Create a scene for the location and place it in the Scenes/Locations folder. The scene name and the folder name must match.

2. Add a game object with a LocationScript Monobehavior (you can copy from an existing location to fill in the necessary fields).

3. Set the terrain object for the location field.

4. Fill the list of habitats, each with a unique name and corresponding splatmap (for flora).

5. For fauna, create multiple ‘boid bounds’ - game object with the BoidBound monobehavior and a box collider. The box collider is used for visualisation. Set the include layers to ‘Nothing’. Scale and move the boid bouds so there is some space for the flora to turn around if it leaves the bounds without it hitting the terrain or the water surface. Set the name for the flora habitats in each boudn object so it matches the ‘habitats’ field in the specie’s json file.

6. In the Main scene, add the name of the location to the Location Names list.

7. In File>Build Settings, add the scene to the Scenes in Build list.

### Adding a new Landscape to Unity

1. Scripts to add to the Terrain Gameobject: Terrain Collider On Start Authoring, Trees To Obstacles on Start Authoring, Terrain Details Cleaner

2. Use the Terrain Toolbox to reset terrain splatmaps, making sure that the current one is saved to a file (You can save to a texture by using Import from Selected Terrain and then Export Splatmaps)

3. Paint a new splatmap for the flora biome by using the 2nd (green) layer. Save that to a texture

4. Mark Read/Write to true on the newly created texture

5. Assign back the original splatmap that is used to drive terrain material

6. Under LocationScript(Game Object)>Biomes, add a new element, set name and set the painted splatmap

7. Flora will spawn on that biome as long as it has the biome name set in it’s json file
