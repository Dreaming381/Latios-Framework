# Latios Framework Packages for DOTS – [0.4.3]

The packages contained in this repository are packages built upon Unity DOTS
which I use for my own personal hobbyist game development. All packages are
licensed under the [Unity Companion
License](https://unity3d.com/legal/licenses/Unity_Companion_License). The
packages may contain code borrowed from official Unity packages and therefore
may be seen as derivative works.

*[0.3.x] users, please read the* [*Upgrade
Guide*](Documentation~/Upgrade%20Guide.md)*!*

**If you have any experience with DOTS, please take** [**this
survey**](https://docs.google.com/forms/d/e/1FAIpQLScgqi7IxpxdPDrAvv_Vif7lf-EjboXEUwO7DxlNLPNZhyqi2A/viewform?usp=sf_link)**!**

## Packages

-   [Core](Documentation~/Core/README.md) – General-purpose utilities and
    bootstrap
-   [Psyshock Physics](Documentation~/Psyshock%20Physics/README.md) – Collision
    and Physics building blocks using an alternate runtime representation
-   [Myri Audio](Documentation~/Myri%20Audio/README.md) – Simple, scalable,
    spatialized sounds and music streaming
-   Kinemation – Authored animation, simulated animation, and everything in
    between (No public release)
-   Mach-Axle AI – An infinite axis utility evaluator designed for high
    throughput (No public release)
-   Life FX – VFX simulations which add immersion to stylized worlds (No public
    release)
-   Ethereal – An NPR renderer authored in reality (No public release)
-   Networking (Name TBD) – Snapshots + input sim = unified, testable,
    inexpensive, and hardware-friendly networking (No public release)

## Getting Started

There are three methods to install the framework package (contains all publicly
released packages).

-   Add package via PackageManager-\>Add package from git URL
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
    -   How To Make an N – 1 Render Loop
    -   How To Spawn an Entity at a Position in a Job
    -   [How To Make Parallel Deterministic
        RNG](Documentation~/Core/Rng%20and%20RngToolkit.md)
    -   How To Find All Tagged Neighbors Within a Radius
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

## Proud Users of Latios Framework

### Open Projects

-   [Latios Space Shooter Sample](https://github.com/Dreaming381/lsss-wip)

## Support

*TL;DR – I try to take issues and feedback as seriously as if this were a
commercial project. Do not hesitate to reach out to me!*

I develop this framework separately from this repository. I will provide the
current snapshot of that code upon request. I promise the code may be terrible.
This may be useful to you if you desire to contribute in an area I am actively
developing. See [Contributing](Documentation~/Contributing.md) for more
information.

-   I will
    -   Take bug reports on the latest release seriously.
        -   If it is broken for you, it is broken for me.
        -   I don’t spend time heavily testing this framework as it is a hobby
            and not my full-time job. I tend to write code while it is fresh in
            my head for completion’s sake, and then test it when I use it later
            or someone reports an issue. If you see an issue, please let me
            know. You aren’t annoying me (and if you are, it is my fault).
        -   Repros and test cases will increase the chances of me publishing a
            fix quickly. But good descriptions go a long ways too.
    -   Consider optimization requests.
        -   As long as Burst is in play, most people care more about features
            than performance, so that’s usually my priority. However,
            optimization is fun! So if you have a performance problem because of
            my code, tell me! I will make it my problem to make it not a
            problem.
    -   Consider documentation and example requests.
        -   XML docs
        -   Markdown docs
        -   Examples highlighting a specific feature that is difficult to
            understand
    -   Consider collaboration requests.
        -   If you are an asset developer (free or paid) and would like to
            officially support this framework, don’t be shy to reach out to me.
        -   If you are working on a game and wish to use my framework, but need
            help integrating it into your project and utilizing its features,
            don’t be shy! I’m pretty easy to reach on the Unity forums.
    -   Consider API suggestions.
        -   API design is a fascinating topic for me. My general philosophy is
            that a competent developer should spend significantly more time
            understanding the problem he/she is solving compared to
            understanding the API he/she is using.
        -   While I spend a great amount of time thinking about API, I am not
            perfect. If you have comments regarding an improvement, naming,
            structure, or anything, tell me. If I agree with a proposed change,
            I will try my best to integrate it.
    -   Consider minor feature requests.
        -   As long as it makes sense in the package, I will add it.
    -   Consider package or major feature proposals.
        -   Please put some thought and design into the proposal.
        -   Please recognize that I do this as a hobby and any time spent on a
            proposed feature is time I am not spending on already planned
            features. I make that call.
        -   Please recognize that I have my personal style and preferences in
            game design which are different than yours.
        -   If you are developing a package and would like me to help maintain
            and distribute it, I will gladly welcome your work to the framework
            family. Of course it will have to meet API standards, but I can help
            you with that.
        -   By far the best way to get me to work on a new package or major
            feature is to collab with me either directly on the feature or in a
            game project where such work is required. I’m almost always looking
            for new collab opportunities!
    -   Consider prioritizing planned features upon request.
        -   If one of the features on my near-term roadmap is a feature you
            desire, let me know, and I can prioritize it and cut you a release
            when it is ready.
-   I will not
    -   Support older versions of packages outside of answering questions and
        assisting in updating to the latest versions.
    -   Guarantee your project does not break when updating between feature
        versions.
        -   I know it is good practice to obsolete things and use
            [FormerlySerializedAs] and provide API updaters. I just don’t care
            enough to bother.
        -   I will integrate pull requests which help with this. So if you want
            to be the nice human being that ensures projects update smoothly and
            know how to write the code to do it, I will gladly accept your
            contributions.
        -   I try to avoid breaking changes for compatibility and bugfix
            releases (the third semantic version number).
    -   Pull pull requests directly.
        -   See the [Contributing](Documentation~/Contributing.md) page for
            details about my workflow.

## A Word of Caution

If you choose to modify any of the packages here licensed under the Unity
Companion License, my understanding is that any modifications, including new
inventions inserted, will belong to Unity as per the terms described by the
license.

Personally, I do not have an issue with this license as it permits me to always
be able to use my inventions in Unity projects and I have no issue if Unity
wants to adopt anything here.
