# Latios Framework Packages for DOTS – [0.5.6]

The packages contained in this repository are packages built upon Unity DOTS
which I use for my own personal hobbyist game development. All packages are
licensed under the [Unity Companion
License](https://unity3d.com/legal/licenses/Unity_Companion_License). The
packages may contain code borrowed from official Unity packages and therefore
may be seen as derivative works.

The current version targets DOTS 0.50 and 0.51. If you are still using Entities
0.17, please use the framework version 0.4.4 instead.

*[0.4.x] users, please read the* [*Upgrade
Guide*](Documentation~/Upgrade%20Guide.md)*!*

**If you have any experience with DOTS, please take** [**this
survey**](https://docs.google.com/forms/d/e/1FAIpQLSfxgFumJvhwjzi-r7L7rGssPoeSLXyV7BeCdCOsqfPWeWY_Ww/viewform?usp=sf_link)**!**

## Packages

-   [Core](Documentation~/Core/README.md) – General-purpose utilities and
    bootstrap
-   [Psyshock Physics](Documentation~/Psyshock%20Physics/README.md) – Collision
    and Physics building blocks using an alternate runtime representation
-   [Myri Audio](Documentation~/Myri%20Audio/README.md) – Simple, scalable,
    spatialized sounds and music streaming
-   [Kinemation](Documentation~/Kinemation%20Animation%20and%20Rendering/README.md)
    – Authored animation, simulated animation, and everything in between
-   Mach-Axle AI – An infinite axis utility evaluator designed for high
    throughput (No public release)
-   Life FX – VFX simulations which add immersion to stylized worlds (No public
    release)
-   Ethereal – An NPR renderer authored in reality (No public release)
-   Networking (Name TBD) – Snapshots + input sim = unified, testable,
    inexpensive, and hardware-friendly networking (No public release)

## Why Use the Latios Framework?

Typically, “frameworks” fall into one of two categories. Either, they are
someone’s collection of convenience classes and extension methods, or they
define a specific architecture and workflow for describing gameplay code.

While the Latios Framework has some of those things, its primary concerns are at
the engine level. Many of its tools and solutions are inspired by GDC
presentations, technical blogs, and research papers. A key focus of the
framework is to make these advanced technologies usable within a DOTS-based
production environment. For technical reasons, it is a “framework”, but it acts
more like a toolkit and stays out of the way. A developer using it should always
feel in control. If not, there’s likely an issue worth bringing to attention.

0.5 marks the end of Phase II, where focus was placed on enabling technologies
in DOTS such as audio and animation. Phase III will focus on gameplay
technologies such as spatial and hierarchical queries and AI tools.

Long term, the Latios Framework’s mission is to dramatically reduce the
development effort required to make highly artistic 3D games and short films.

## Getting Started

There are three methods to install the framework package (contains all publicly
released packages).

-   Add package via Package Manager -\> Add package from git URL
-   Add via [OpenUPM](https://openupm.com/packages/com.latios.latiosframework/)
-   Clone or submodule this repository into your project’s Packages folder

After installing the framework package, follow the instructions in the first
section [here](Documentation~/Core/Getting%20Started.md). You may also want to
look through the [compatibility
guide](Documentation~/Installation%20and%20Compatibility%20Guide.md).

Getting Started pages and documentation are provided with each module.

## Learning Resources

-   Videos
    -   [Intro Tour Video
        Playlist](https://www.youtube.com/watch?v=UGKtIZOolEo&list=PLFME_M84NcPylGB41xAzh2bbbT8nhb_a0)
-   Official How-To Guides
    -   [How To Make an N – 1 Render
        Loop](Documentation~/How-To%20Guides/How%20To%20Make%20an%20N%20-%201%20Render%20Loop.md)
    -   [How To Spawn an Entity at a Position in a
        Job](Documentation~/How-To%20Guides/How%20To%20Spawn%20an%20Entity%20at%20a%20Position%20in%20a%20Job.md)
    -   [How To Make Parallel Deterministic
        RNG](Documentation~/Core/Rng%20and%20RngToolkit.md)
    -   [How To Find All Tagged Neighbors Within a
        Radius](Documentation~/How-To%20Guides/How%20To%20Find%20All%20Tagged%20Neighbors%20Within%20a%20Radius.md)
-   FAQ
    -   [Can I use Core and Myri with Unity or Havok
        Physics?](Documentation~/FAQ/FAQ%20-%20Can%20I%20Use%20Core%20and%20Myri%20with%20Unity%20or%20Havok%20Physics.md)
    -   [How Do I Choose Between Hybrid, Shared, and Managed Components for My
        Data?](Documentation~/FAQ/FAQ%20-%20Component%20Types.md)
-   Optimization Adventures
    -   [Find Pairs
        1](Documentation~/Optimization%20Adventures/Part%201%20-%20Find%20Pairs%201.md)
    -   [Build Collision Layer
        1](Documentation~/Optimization%20Adventures/Part%202%20-%20Build%20Collision%20Layer%201.md)
    -   [Space Sky
        1](Documentation~/Optimization%20Adventures/Part%203%20-%20Space%20Sky%201.md)
    -   [Command Buffers
        1](Documentation~/Optimization%20Adventures/Part%204%20-%20Command%20Buffers%201.md)
    -   [Find Pairs
        2](Documentation~/Optimization%20Adventures/Part%205%20-%20Find%20Pairs%202.md)
    -   [Collider Cast
        1](Documentation~/Optimization%20Adventures/Part%206%20-%20ColliderCast%201.md)
    -   [Frustum Culling
        1](Documentation~/Optimization%20Adventures/Part%207%20-%20Frustum%20Culling%201.md)

## Proud Users of Latios Framework

### Tools

-   [DMotion](https://github.com/gamedev-pro/dmotion) – Open source animation
    state machine powered by Kinemation

### Open Projects

-   [Latios Space Shooter Sample](https://github.com/Dreaming381/lsss-wip)

## Support

*TL;DR – I try to take issues and feedback as seriously as if this were a
commercial project (or for the cynical, to shame commercial projects). Do not
hesitate to reach out to me!*

### Issues

This is a hobby project and not my full-time job, so I cut corners and don’t
spend a lot of time testing things. Sometimes I write code while an idea is in
my head and just leave it there. Bugs will sneak in.

However, **bugs are infuriating!**

If you see a confusing error, send me a description of what you were doing and a
stack trace with line numbers and the version you are using. You can use the
GitHub issues, GitHub discussions, the DOTS forums, Unity PMs, or emails.
Usually, I will find a bug fix locally, and provide instructions on how to apply
the fix yourself. Then I will roll out the bug fix in my next release.

For strange behavior that doesn’t trigger errors, a repro is the only way to
guarantee the issue gets diagnosed, but a good description can go a long ways
too.

For performance issues, if you can’t send me a repro, send me a profile capture.
It doesn’t matter if the issue makes your game unplayable or if you just want
one task to be a couple hundred microseconds faster. I’m interested!

### Feature Requests

Please send feature requests, even if it is already listed on one of the
near-term roadmaps. It helps me prioritize. Requests for small utility functions
or other simple concerns can usually be squeezed in patch releases. I always
reserve full discretion. Try to propose your use case, and focus less on a
proposed solution.

### Learning Resource Requests

If you see gaps in the documentation, or are struggling to understand features,
let me know. If you would like more demos and samples, also let me know what you
would like an example of. I won’t build your game idea for you, but simple
things I can try and squeeze in.

### Derivatives, Collaboration, and Contribution

I develop this framework separately from this repository. I will provide the
current snapshot of that code upon request. I promise the code may be terrible.
This may be useful to you if you desire to contribute in an area I am actively
developing. See [Contributing](Documentation~/Contributing.md) for more
information.

Because I often validate changes manually, I usually won’t merge pull requests.
I’m working on a new workflow that will preserve contributor commits in the
history. But if I run into issues, I will always prioritize getting changes into
releases.

If you are developing your own packages on top of this framework, commercial or
open source, feel free to reach out to me for suggestions, guidance, or to
establish your stake in the areas that concern you. It is important to me that
such dependent technologies are successful. For open source projects, I may even
send a pull request.

### Feature Releases and Compatibility

I do not promise backwards compatibility between feature releases (0.X). I will
have upgrade guides detailing all the breakages and what to change. But it will
be a manual process.

Patch releases (0.5.X) will always preserve backwards compatibility back to the
last feature release.

While I will provide tips and suggestions if you use older releases, I will not
publish patch releases for older versions. (There might be an exception to this
rule during the transition to DOTS 1.0, but that is TBD.)

## A Word of Caution

If you choose to modify any of the packages here licensed under the Unity
Companion License, my understanding is that any modifications, including new
inventions inserted, will belong to Unity as per the terms described by the
license.

Personally, I do not have an issue with this license as it permits me to always
be able to use my inventions in Unity projects and I have no issue if Unity
wants to adopt anything here.
