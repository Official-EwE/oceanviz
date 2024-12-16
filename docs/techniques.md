# Techniques
### Splitscreen view
Oceanviz supports visualizing up to 4 different scenarios at the same time by splitting the screen vertically. Instead of simulating the virtual world independently for each screen, we chose an approach where the virtual world is only simulated once, and then depending on which quadrant an entity is in, the entity can be made invisible in order to show a difference in population of a particular species. This technique of using a single world, and just making the fish appear and disappear based on their screen position, gives us visual continuity when a fish swims across multiple split screens; it is also much more performant than simulating multiple worlds in parallel. The downside is that we are more limited in terms of what can be differentiated on different screens: it’s currently impossible to show different seabed or landscape per screen region.  Currently, we can vary the population of any flora or fauna, and the turbidity of the water.

```
Implementation Detail:

For this to work, Hybrid per Instance Shader Declaration must be on for the entity shader. This ensures that each instance of the fish can be turned on and off based on their position on screen.

The way it works now in ECS is on value changed a Flock object sends the current amount of views, visible percentages per view and a flag to update the shaders to the proper boidSchool entity via the EntityManager (which is the main way of communicating with the ECS). Next the BoidSchoolSpawnSystem (should be renamed BoidSchoolManagementSystem since it now does much more) if it hits a set flag requesting for shader update iterates over boid entities from that school and does a calculation per boid and per view if the boid in the specified percentage of that view, and sets the shader override component accordingly.
```


## Schooling Fish
Typically, for a game engine to control thousands of moving fish is a very resource-intensive task. Fortunately Unity has a system called ECS that offers a data-oriented approach that greatly improves performance and scalability in scenarios like large schools of fish, by allowing the system to operate on tons of data in parallel.

```
Implementation Detail:

ECS works like this: you start with a prefab or a GameObject that has an Authoring script. The script is used for the process of 'Baking', meaning a conversion from a GameObject with a MonoBehavior script (standard way of doing things in unity, so an object that is in regular scene hierarchy and can have both properties and methods) to an Entity, which is data only (basically properties but more performant).
You place those in a 'SubScene', which is a place that will automatically do the Baking at the start.
In the example, the Subscene has BoidSchool GameObjects, Target GameObjects, and Obstacles GameObjects placed this way. These are all converted to Entities, to be operated on by Systems.

First, we have the BoidSchoolSpawnSystem, which operates on BoidSchool entities, reads the Prefab, the Bounds, and the Count fields, and spawn Boid entities accordingly. we modified it so that the Count is set to 0 after spawning, so if we want to spawn more Boids in that school (or flock), we can set the Count to specify how many to add.

Next there is BoidSystem, which does the all the obstacle avoidance separation/alignment steering magic. It's surprisingly small and well documented which is good. It also gets its performance by dividing boids into cells.

Now, both those systems are using an ISystem interface instead of creating a System class. In practice, this means they can't really be controlled from the outside, for stuff like adding/destroying entities, adding new schools of fish etc. To do that, one needs a system that inherits from a SystemBase class, which gives you a real class. In that class we can get access to an EntityManager to create and destroy entities, modify the fields of existing entities, and check for Input. 

The boid steering is controlled with weights for separation, alignment, target following and obstacle aversion. In the base system the boids used always the closest target, and the target were animated with keyframes. we modified it so that each school of fish now spawns alongside its own target, and the target changes position within the bounds in 1-10 second random intervals. Previously all boids moved as one big clump, not there is a visual separation of each school/flock.
```

## Visibility Culling
Culling is the removal or relocation of objects in order to serve 2 main purposes:
Reduce the number of objects that the game engine has to process when they are not visible to the viewer in order to increase performance.
Moving entities outside of the camera viewing frustum to within the view of the camera to ensure a predictable amount of items on screen.
```
Implementation Detail:

TBW
```

## Entity Animation
Giving fish and vegetations realistic swimming movements and natural waving motions is a key part to making our underwater scene look alive. Our approach, which is both performant and flexible, has been extensively described in the `assets.md` document.


## Streaming Asset
All flora and fauna data such as the 3d model, image textures, and behavior json file are dynamically loaded on the fly. This makes iterating on assets much faster and because changes made to these assets are reflected while the game is running continuously. 

This also means the visualization only has to load the asset needed, instead of all the assets available in the library.

