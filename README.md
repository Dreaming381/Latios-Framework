Latios Framework Packages for DOTS
==================================

The packages contained in this repository are packages built upon Unity DOTS
which I use for my own personal hobbyist game development. All packages are
either licensed under the [MIT License](https://opensource.org/licenses/MIT) or
the [Unity Companion
License](https://unity3d.com/legal/licenses/Unity_Companion_License). The
packages licensed under the Unity Companion License may contain code borrowed
from official Unity packages and therefore may be seen as derivative works.

Packages
--------

-   Core – General-purpose utilities and bootstrap

-   Physics – Collision and Physics building blocks using an alternate runtime
    representation

-   Life FX – VFX simulations which add immersion to stylized worlds

-   Kinemation – IK over FK animation authoring and runtime with post-sample
    oscillations

Getting Started
---------------

The packages are distributed in multiple forms across multiple branches. Master
contains the full framework, while the other branches each contain an individual
package for those who do not wish to use the full framework.

Chose the SHA of the package you desire or clone or download the packages into
your packages folder of your project and modify your manifest.json. In the
future Unity may support nested package directories in a GitHub repository. (Let
me know if that time comes!)

Getting Started pages and documentation are provided with each package.

Support
-------

I develop these packages in a separate private repository. I will provide the
current snapshot of that code upon request. I promise the code will be terrible.
This may be useful to you if you desire to contribute in an area I am actively
developing. See Contributing for more information.

-   I will

    -   Take bug reports on the latest release seriously.

        -   If it is broken for you, it is broken for me.

        -   I don’t spend time heavily testing this framework as it is a hobby
            and not my full time job. I probably will only notice issues when I
            try to use something in a game jam and it breaks, unless you alert
            me.

        -   Repros and test cases will increase the chances of me publishing a
            fix quickly.

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

    -   Consider prioritizing planned features upon request.

        -   If one of the features on my near-term roadmap is a feature you
            desire, let me know, and I can prioritize it and cut you a release
            when it is ready.

-   I will not

    -   Support older versions of packages outside of answering questions and
        assisting in updating to the latest versions.

    -   Guarantee your project does not break when updating.

        -   I know it is good practice to obsolete things and use
            [FormerlySerializedAs] and provide API updaters. I just don’t care
            enough to bother.

        -   I will integrate pull requests which help with this. So if you want
            to be the nice human being that ensures projects update smoothly and
            know how to write the code to do it, I will gladly accept your
            contributions.

    -   Pull pull requests directly.

        -   See the Contributing page for details about my workflow.

A Word of Caution
-----------------

If you choose to modify any of the packages here licensed under the Unity
Companion License, my understanding is that any modifications, including new
inventions inserted, will belong to Unity as per the terms described by the
license.

Personally, I do not have an issue with this license as it permits me to always
be able to use my inventions in Unity projects and I have no issue if Unity
wants to adopt anything here.
