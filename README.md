
# Oceanviz 3 is:
- An open-source API for 3D visualization of underwater environment
- A library of 3D assets of marine life and environments
- A framework that can be extended to visualize more marine ecosystems
- Built on top of the Unity game engine

![image](https://github.com/user-attachments/assets/f67086b1-0714-4f7b-8ef3-9e22074cdfec)

# Features:
Split-screen: Visualize up to 4 scenarios simutaneously by varying the water turbidity and number of species per screen region:
![Screenshot 2024-12-15 110259](https://github.com/user-attachments/assets/f90c5584-6a74-4b1c-b0ef-72e457518c2e)

Realism: The environment is generated from a library of assets based on rules to create a rich and believable world:
![Screenshot 2024-12-15 105744](https://github.com/user-attachments/assets/767404d8-a0e4-4878-8b8d-0bc3c213fca4)

Performance: Uses numerous SofA techniques to ensure high performance even with massive numbers of fish on-screen:
![Screenshot 2024-12-15 111922](https://github.com/user-attachments/assets/0b014453-1c63-435e-850d-d0df4c1347c8)

Programmable: Uses simple JSON data structure to drive the visualization:
![Screenshot 2024-12-15 112945](https://github.com/user-attachments/assets/7dd5f5d1-1662-40fa-818b-5af997eb9ae7)

Asset Library: Over 50 ready-to-use entities including fish, mammals, birds, invertebrates, and manmade objects:
![Screenshot 2024-12-15 113920](https://github.com/user-attachments/assets/b08c19f7-2c7c-4a11-b67d-834147fb6cfe)


# Development:
### Software Requirement
- [Unity LTS Release 2022.3.20f1](https://unity.com/releases/editor/qa/lts-releases)
  - Unity will also install Visual Studio which is needed for software development
- [Blender 4.2](https://www.blender.org/download/)
  - This is needed if you want to make changes or additions to 3D assets library
- Development is done on Windows 10/11, but the source code is cross-platform since Unity itself runs on Windows, MacOS, and Linux.

### Hardware Requirement
- X64 architecture Quad Core Intel i5 or Ryzen 5 processor
- 8GB of RAM, 16GB preferred for development
- DirectX12 capable GPU with 4GB of video RAM (Geforce 20xx+ or Radeon RX 50xx+ or higher)
- Integrated graphics are also supported if it's relatively modern (Intel Iris Xe or AMD Radeon)

### Quick Start
1. Install Unity Hub launcher and the corresponding version of Unity outlined in the Software Stack section.
2. Clone this repository
3. Add the project folder `oceanviz3` to Unity Hub, then double click the project to launch the editor.
4. Once the project is open in Unity, press the triangle 'Play' button at the top of the screen to start the visualization.
5. Once the visualization is loaded, use the on-screen UI on the left side to add/remove species, and change environment.


### API Access
Because Oceanviz is designed to be an open-ended framework, the source provided acts a starting point for you to build your own ecosystem.

There are 2 primary ways to control Oceanviz:
1. Via the on-screen UI
2. Via an API by feeding the visualization a JSON data structure.

Detailed documentation for developers can be found in the /docs directory.


# Credits:
Oceanviz 3 is created by Ecopath International Initiative by Paweł Łyczkowski, Pietro di Chito, Mike Pan, and Jeroen Steenbeek.
