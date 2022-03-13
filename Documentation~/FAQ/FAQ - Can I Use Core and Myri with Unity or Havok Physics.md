# FAQ – Can I use Core and Myri with Unity or Havok Physics?

Short answer: Yes!

In fact, you can even use Psyshock in the same project as one that uses Unity or
Havok Physics.

Psyshock is the least-invasive module in the Latios Framework. The only system
it contains is a conversion system which adds its own Collider type to entities.
And if you want to save the extra runtime memory and chunk capacity, you can
even disable this system by following [this
guide](../Installation%20and%20Compatibility%20Guide.md).

Psyshock is just a bunch of types and algorithms for implementing custom game
mechanics and supporting future technology. If you don’t want to use it, you can
just ignore it.
