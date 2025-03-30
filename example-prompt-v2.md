# Create a "Collect the Cubes" game
    Objective: The player controls a simple 3D character (like a sphere or capsule) that moves around a flat 3D environment to collect floating cubes before a timer runs out.
    Win Condition: Collect all the cubes (e.g., 5–10) to win.
    Lose Condition: Timer runs out before all cubes are collected.

## Steps    
    Create a 3D plane in the scene and position it as the ground.
    Add a 3D sphere to the scene as the player object.
    Attach a Rigidbody component to the sphere.
    Create a new script called "PlayerMovement" and attach it to the sphere.
    Add five 3D cubes to the scene, positioning them at different spots above the ground.
    Add a Collider component to each cube and set it as a trigger.
    Create a new script called "Collectible" and attach it to each cube.
    Create an empty GameObject called "GameManager" in the scene.
    Create a new script called "GameController" and attach it to the GameManager.
    Add a UI Text element to the scene for displaying the score.
    Add a second UI Text element to the scene for displaying the timer.
    Create a UI Text element for a win message and set it to be invisible by default.
    Create a UI Text element for a lose message and set it to be invisible by default.
    Save the scene.