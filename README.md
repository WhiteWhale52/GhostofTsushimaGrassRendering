# An Attempt at an Open World Grass Rendering

### Inspiration
* This project was inspired by **Ghost of Tsushima**. The focus is the replicate its amazing grass rendering system which includes
  *  The curvature of the grass blades following a quadratic bezier curve
  *  The light reflection
  *  Apply a scrolling global wind texture
  *  Add player interaction to the grass
  *  Use Chunking/Tiling to separate the world and run a compute shader per tile
  *  Use Voronoi noise to create variations in grass groups, similar to areas of high nutrition and areas of low nutrition
  *  Smoothly transition between a 15-vert grass blade to 7 vert grass blade to simulate different LOD
    
