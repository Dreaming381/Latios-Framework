![](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/554a583e217bfe5bf38ece0ed65b22c33711afc6/media/bf2cb606139bb3ca01fe1c4c9f92cdf7.png)

# Latios Framework for Unity ECS – [0.11.5]

The Latios Framework is a powerful suite of high-performance low-level APIs and feature-sets for Unity’s ECS which aims to give you back control over your gameplay. If you like the general paradigms, syntax, and workflows of Unity’s ECS, but find Unity’s offerings to be incomplete or frustratingly full of quirky unintuitive details, then this framework may be exactly what you need to achieve your vision.

The Latios Framework does not replace Unity’s ECS, but rather complements it with additional APIs and tools. In some cases, the Latios Framework may override Unity ECS’s underlying mechanisms to provide more features or improve performance. Desktop platforms are supported out-of-the-box. Other platforms may require additional effort (i.e. compiling native plugins) to achieve functionality and performance benefits.

Originally, this framework was for my own personal hobbyist game development, and in a sense, still is. However, after several years of development, it has proven a valuable resource to the Unity ECS community. It is provided here free to use for personal or commercial usage and modification. All modules are licensed under the [Unity Companion License](https://unity3d.com/legal/licenses/Unity_Companion_License). The modules may contain code borrowed from official Unity packages and therefore may be seen as derivative works. Despite this, the Latios Framework contains many adaptations of top-class solutions in the industry (see [Third Party Notices](THIRD%20PARTY%20NOTICES.md)) as well as original inventions geared towards Unity’s ECS.

This version targets Entities 1.3.5 with ENTITY_STORE_V1 and a minimum editor version of 2022.3.36f1.

*[0.10.x] users, please read the* [*Upgrade Guide*](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Upgrade%20Guide.md)*!*

**If you have any experience with DOTS, please take** [**this survey**](https://forms.gle/iQ7Hue7atFZ2GrM16)**!**

## Modules

The Latios Framework contains multiple **modules**, each of which contain public API for your own use. Additionally, each module may contain **addons**. Addons are community contributed features, and consequently may have different design philosophies or support guarantees as other features. With that said, some addon authors are very active and offer support and reliability that surpasses native features.

Modules and addons are disabled by default and are installed via a custom bootstrap. Bootstrap templates are provided in the Assets Create menu.

### Core

[Core](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Core/README.md) is an essentials kit for handling common programming concerns in Unity’s ECS. It contains many features you might have heard of such as Rng, Blackboard Entities, Collection Components, Instantiate Command Buffers, Smart Blobbers, Explicit System Ordering, and Baking Bootstraps. But there are many more features around. If there is a common “hard” problem in ECS, there’s a good chance Core has a tool to address it.

**Common Reasons to Use:**

-   You use one of the other modules
-   You want one of the features

**Common Reasons to Avoid:**

-   You move a large amount of entities between worlds in the middle of gameplay

### QVVS Transforms

[QVVS Transforms](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Transforms/README.md) provide custom transforms systems based on the concept of QVVS transforms, which are vector-based transforms that can represent non-uniform scale without ever creating shear. This module comes with a fully functional custom transform system with automatic baking and systems, offering more features, performance, and determinism than what is shipped with Unity.

If you wish to use Unity Transforms instead, you can enable a compatibility mode for all other modules using the scripting define LATIOS_TRANSFORMS_UNITY. Some features in the other modules will be disabled when you do this.

**Common Reasons to Use QVVS Transforms:**

-   You want a good transform hierarchy solution

**Common Reasons to Use Unity Transforms:**

-   You use Unity Physics, Unity Character Controller, or Unity NetCode.

### Psyshock

[Psyshock Physics](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Psyshock%20Physics/README.md) is a physics and spatial query module focused on user control. While most physics engines provide out-of-the-box simulation, Psyshock instead provides direct access to the underlying algorithms so that you can craft the perfect physics simulation for your game and not waste any computation on things you don’t need. Psyshock’s Collision Layers can be built directly from Entity Queries, removing all the archetype guesswork out of collisions and triggers.

**Common Reasons to Use Psyshock:**

-   You use QVVS Transforms
-   You only need collision detection
-   You want more control over physics
-   Unity Physics events system is too expensive

**Common Reasons to Use Unity Physics:**

-   You want an out-of-the-box full simulator with zero code

### Myri

[Myri Audio](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Myri%20Audio/README.md) is an out-of-the-box pure ECS audio solution. It features 3D spatialization of both looping and non-looping audio sources, multiple listeners, directional and non-directional sources, and a voice combining feature to support massive amounts of sources at once. Playing audio is as simple as instantiating prefabs.

**Common Reasons to Use:**

-   You want something simple and easy

**Common Reasons to Ignore:**

-   You can afford FMOD

### Kinemation

[Kinemation](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Kinemation%20Animation%20and%20Rendering/README.md) Animation and Rendering provides authored animation, simulated animation, and everything in between. It includes an overhauled Entities Graphics for significantly improved performance of both skinned and non-skinned entities including true frustum culling and LOD crossfade support. It also provides a comprehensive API for injecting custom effects into ECS rendering.

On the animation side, Kinemation supports bone entity and optimized bone buffer configurations. It includes utilities for inertial blending. And for animation clips it leverages ACL, a powerful high quality animation compression solution used in AAA titles such as Rise of the Tomb Raider, Fortnite, and Valorant.

**Common Reasons to Use:**

-   You use QVVS Transforms
-   You want better rendering performance and features related to LODs, light probes, and deforming meshes
-   You want to customize and extend ECS rendering with access to the culling logic
-   You prefer code-driven animation workflows and inertial blending
-   You wish to build your own animation visual tool tailored to your project

**Common Reasons to Avoid:**

-   You absolutely require a robust out-of-the-box code-free skeletal animation solution for only a small number of entities

### Calligraphics

[Calligraphics](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Calligraphics/README.md) is a world-space text rendering module. It uses TextCore fonts and formats text to be rendered via the Kinemation rendering pipeline, complete with custom ECS material property support. The text can be animated with the built-in tweening engine, or you can make your own animations with the glyph mapping API. Rich text tags are supported, with much of the implementation being ported from TextMeshPro and made Burst-compatible. Use Calligraphics for world-space labels, dialog, player names, and damage numbers.

**Common Reasons to Use:**

-   You use QVVS Transforms
-   You want animated text
-   You are using any other module

**Common Reasons to Avoid:**

-   TextMeshDOTS works and fully covers your needs

### Mimic

[Mimic](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Mimic/README.md) provides behavioral replicas of popular solutions within the Unity ecosystem, rewritten to leverage the features of the other modules and the performance benefits of Unity’s ECS. With Mimic, teams can continue to use familiar and proven authoring tools and workflows while simultaneously being fully invested into Unity ECS and the Latios Framework.

-   Addons
    -   Mecanim by Sovogal – Replication of Unity’s Animator Controller state machine with support for blend trees, interrupts, root motion, and events.

### Future Modules

-   Unika – A high-performance scripting solution including support for interfaces and coroutines using source generators
-   Life FX – VFX simulations which add immersion to stylized worlds
-   Mach-Axle AI – A utility AI evaluator designed for high throughput
-   Unnamed Networking – Something fast and flexible at scale

## Why Use the Latios Framework?

Typically, “frameworks” fall into one of two categories. Either, they are someone’s collection of convenience classes and extension methods, or they define a specific architecture and workflow for describing gameplay code.

While the Latios Framework has some of those things, its primary concerns are at the engine level. Many of its tools and solutions are inspired by GDC presentations, technical blogs, and research papers. A key focus of the framework is to make these advanced technologies usable within a DOTS-based production environment. But another common theme is fixing or providing alternatives for fundamental design issues in the official ECS packages. For technical reasons, it is a “framework”, but the individual APIs act more like a toolkit and stay out of the way. A developer using it should always feel in control. If not, there’s likely an issue worth bringing to attention.

In addition, the Latios Framework strives to fix multiple fundamental performance and behavior issues within Unity’s ECS packages. The results of such efforts are best demonstrated in [this video](https://youtu.be/AgcRePkWoFc). For a complete breakdown of these changes with each configuration and bootstrap, [check out this guide](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/What%20Parts%20of%20ECS%20Does%20the%20Latios%20Framework%20Change.md).

0.5 marked the end of Phase II, where focus was placed on enabling technologies in Unity ECS such as audio and animation. Current Phase III development focuses on modernizing the technology for Entities 1.X and facilitating gameplay design.

Long term, the Latios Framework’s mission is to dramatically reduce the development effort required to make highly artistic 3D games and short films.

## Getting Started

There are three methods to install the framework package (contains all publicly released modules).

-   Clone or submodule this repository into your project’s Packages folder (recommended for contributors or those wanting faster bugfixes and updates)
-   Add package via Package Manager -\> Add package from git URL
-   Add via [OpenUPM](https://openupm.com/packages/com.latios.latiosframework/)

After installing the framework package, follow the instructions in the first section [here](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Core/Getting%20Started.md). You may also want to look through the [compatibility guide](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Installation%20and%20Compatibility%20Guide.md).

Getting Started pages and documentation are provided with each module.

## Learning Resources

-   Social
    -   [Discord](https://discord.gg/DHraGRkA4n)
    -   [Forum Thread](https://forum.unity.com/threads/797685/)
-   [Documentation (Click on any .md file)](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Upgrade%20Guide.md)
-   [Mini Demos](https://github.com/Dreaming381/LatiosFrameworkMiniDemos)

## Proud Users of Latios Framework

### Forks and Extensions

-   [TextMeshDots](https://github.com/Fribur/TextMeshDOTS) – A standalone fork of Calligraphics

### Open Projects

-   [Latios Space Shooter Sample](https://github.com/Dreaming381/lsss-wip)
-   [Free Parking](https://github.com/Dreaming381/Free-Parking)

## Support

*TL;DR – I try to take issues and feedback as seriously as if this were a commercial project (or for the cynical, to shame commercial projects). Do not hesitate to reach out to me!*

### Issues (Bugs or Performance)

This is a hobby project and not my full-time job, so I cut corners and don’t spend a lot of time testing things. Sometimes I write code while an idea is in my head and just leave it there. Bugs will sneak in.

However, **bugs are infuriating!**

If you see a confusing error, send me a description of what you were doing and a stack trace with line numbers and the version you are using. You can use the GitHub issues, GitHub discussions, Discord, the DOTS forums, Unity PMs, or emails. Usually, I will find a bug fix locally, and provide instructions on how to apply the fix yourself. Then I will roll out the bug fix in my next release.

For strange behavior that doesn’t trigger errors, a repro is the only way to guarantee the issue gets diagnosed, but a good description can go a long ways too.

For performance issues, if you can’t send me a repro, send me a profile capture. It doesn’t matter if the issue makes your game unplayable or if you just want one task to be a couple hundred microseconds faster. I’m interested!

### Feature Requests

Please send feature requests, even if it is already listed on one of the near-term roadmaps. It helps me prioritize. Requests for small utility functions or other simple concerns can usually be squeezed in patch releases. I always reserve full discretion. Try to propose your use case, and focus less on a proposed solution.

### Learning Resource Requests

If you see gaps in the documentation, or are struggling to understand features, let me know. If you would like more demos and samples, also let me know what you would like an example of. I won’t build your game idea for you, but simple things I can try and squeeze in.

### Derivatives, Collaboration, and Contribution

I develop this framework separately from this repository scattered across various projects. I will provide the current snapshot from any of those projects upon request. I promise the code may be terrible. This may be useful to you if you desire to contribute in an area I am actively developing. See [Contributing](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Contributing.md) for more information.

If you are developing your own packages on top of this framework, commercial or open source, feel free to reach out to me for suggestions, guidance, or to establish your stake in the areas that concern you. It is important to me that such dependent technologies are successful. For open source projects, I may even send a pull request.

### Feature Releases and Compatibility

I do not promise backwards compatibility between feature releases (0.X). I will have upgrade guides detailing all the breakages and what to change. But it will be a manual process.

Patch releases (0.11.X) will always preserve backwards compatibility back to the last feature release.

While I will provide tips and suggestions if you use older releases, I will not publish patch releases for older versions.

### Free Parking for All of the Above

If you are experimenting with features, vouching for a new feature, wish to collaborate, or are trying to hunt down a troublesome issue, the best way to ensure success is to make a dev dungeon in Free Parking. This provides an easy-to-access collaborative space for fast iteration, and can also serve as a regression test for further development.

## Special Thanks To These Awesome Contributors

If you would like to be added to this list, see [Contributing](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/main/Contributing.md) for how to get started.

-   Sovogal – Primary author of Mimic’s Mecanim addon and made significant contributions to the Calligraphics module (including the name)
-   canmom – Android support, Kinemation baking fixes, and build fixes
-   Fribur – Calligraphics rich text overhaul and shader improvements
-   Dechichi01 – Various fixes and improvements for Core, Psyshock, and Kinemation
-   Anthiese – Mac OS support
-   Lewis – Improvements to `EntityWith<>` and `EntityWithBuffer<>`
-   Everyone else who reported bugs and made the Latios Framework more stable for everyone

## A Word of Caution

If you choose to modify any of the contents here licensed under the Unity Companion License, my understanding is that any modifications, including new inventions inserted, will belong to Unity as per the terms described by the license.

This license is subject to change to one that allows pieces of the Latios Framework not developed by Unity to be used in other ecosystems. If this is something you desire, feel free to discuss in the Latios Framework Discord.

If anyone at Unity sees this, know that you have full permission to use anything in here without attribution unless the code falls under one of the third-party licenses (which will be documented via comments directly next to the relevant code).
