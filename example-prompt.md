Create an endless runner game based on the Google Dinosaur game concept.

    Scene Setup:

        Create a new 2D scene named "EndlessRunner".

        Configure the Main Camera for 2D orthographic view.

    Player Character:

        Create a GameObject for the Player (e.g., a simple sprite or 2D shape like a square).

        Position the Player towards the left side of the screen, slightly above the ground level.

        Add appropriate 2D physics components (Rigidbody2D, Collider2D) to the Player. Configure gravity.

        Create a PlayerController script and attach it to the Player.

            Implement jump functionality triggered by player input (e.g., Spacebar, mouse click, or screen tap). This should apply an upward force.

            Prevent double-jumping unless intended (check if grounded).

            Detect collisions, specifically with objects tagged as "Obstacle".

    Ground:

        Create at least two Ground GameObjects (e.g., long thin sprites or shapes) that can be placed end-to-end.

        Add Collider2D components to the Ground GameObjects so the player can stand on them.

        Create a script (e.g., GroundScroller) to manage ground movement.

            Implement continuous scrolling movement from right to left for the ground segments.

            Implement logic to reposition ground segments that move off-screen to the left back to the right side, creating an infinite loop.

    Obstacles:

        Create at least one Obstacle prefab (e.g., a different sprite or shape representing a cactus).

        Add a Collider2D component to the Obstacle prefab.

        Assign a specific tag (e.g., "Obstacle") to the Obstacle prefab.

        Create an empty GameObject named ObstacleSpawner.

        Create an ObstacleSpawner script and attach it.

            Implement logic to periodically spawn Obstacle prefabs at a set position off-screen to the right.

            Introduce random variation in the time between spawns.

            (Optional) Implement logic to choose randomly between different obstacle types if more than one prefab is created.

        Create an ObstacleMover script and attach it to the Obstacle prefab(s).

            Implement movement for spawned obstacles from right to left at the game's current speed.

            Implement logic to destroy obstacles once they move off-screen to the left.

    Game Management:

        Create an empty GameObject named GameManager.

        Create a GameManager script and attach it.

            Manage the overall game state (e.g., Initializing, Playing, GameOver).

            Track the player's score, increasing it over time while the game state is "Playing".

            Control the game's speed, gradually increasing the scrolling speed of the ground and obstacles over time.

            Implement Game Over logic: triggered when the Player collides with an "Obstacle". This should stop all movement (player, ground, obstacles) and change the game state.

            Implement Restart logic: allow the player to restart the game (e.g., by pressing a key or button) after a Game Over, resetting the score, speed, and scene elements.

    User Interface (UI):

        Create a UI Canvas.

        Add a UI Text element to display the current score, updated by the GameManager.

        Add UI elements for the Game Over screen (e.g., "Game Over" text, final score display, restart instructions). These should be hidden initially and shown when the game state changes to "GameOver".