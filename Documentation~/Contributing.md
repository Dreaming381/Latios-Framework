# Contributing

Oh hi! Awesome of you to stop by!

No. I don’t want your money. And no, I don’t drink coffee.

But if you want to write code (or commission someone I suppose), that might be
code I don’t have to write. That means more time for me to invent stuff. I like
this plan!

Here’s how to contribute new features.

## Picking a Feature

The first step is to pick a feature. Here’s a list of features to choose from.
If you don’t see the feature you want to explore listed here or have questions
regarding one of these features, please reach out to me.

### Core

-   Easy
    -   GetComponent/Buffer(s)InRoot/Hierarchy/Children
    -   FindEntity/EntitiesWithTypeInHierarchy/Children
    -   RemoveComponentCommandBuffer
-   Advanced
    -   AddComponentCommandBuffer
    -   AddSharedComponentCommandBuffer
    -   InstantiateCommandBufferWithRemap
    -   InstantiateCommandBuffer – dynamic buffer variants
    -   InstantiateCommandBuffer – children initialization variants
    -   Collection Component unmanaged indexing
    -   Blackboard Entity runtime editor

### Psyshock

-   Easy
    -   SpeculativeAabbFrom
    -   Spring Force Utility
    -   PID Controller
-   Advanced
    -   Character Controller Support Plane Solver
    -   AreOverlapping(Sphere/Capsule/Box)
    -   FitToMesh(Sphere/Capsule/Box)

### Myri

-   Easy
    -   Audio Source Gizmos and Handles
    -   Audio Source Streamlined Inspector
    -   Audio Layers
-   Advanced
    -   Audio Clip Compression and Decompression Library
    -   Multiband EQ Kernel
    -   Multiband Limiter Kernel
    -   Vertical Spatialization Profile

### Kinemation

-   Easy
    -   Platform support (requires modifying ACLUnity CMake)
    -   Blending and IK utiltities
-   Advanced
    -   Cage mesh deformation

## Developing Using a Public GitHub Account

If you would like to use git to develop a new feature, follow these steps:

1.  Fork the Latios Framework from GitHub.
2.  Create a Unity Project where you would like to develop your feature. This
    can be a new or existing project. No one but you will see this project.
3.  Clone or Submodule your fork of the Latios Framework into the Packages
    folder of your project.
4.  If you use a code formatter, format all code in the Latios Framework and
    commit it. (If you know in advance which files you need to modify, you can
    choose to only format those files instead. But make sure you do not commit
    any other formatted files in future commits.)
5.  Make your changes and test using your project.
6.  Make as many commits as you like.
7.  Do not worry about documentation or version numbers. Documentation updates
    are nice, but not necessary. Unity Tests are also optional.
8.  Push your changes to your fork.
9.  Make a pull request.
10. If your PR is closed, that means a version of the feature you implemented
    has landed in an internal repo and will arrive in a future release.

## Developing Without GitHub

If you use some other source control mechanism or an internal git host, follow
these steps:

1.  Create a Unity Project where you would like to develop your feature. This
    can be a new or existing project. No one but you will see this project.
2.  Download, Clone, or Submodule the Latios Framework into the Packages folder
    of your project.
3.  If you use a code formatter, format all the code in the Latios Framework and
    save a copy in a zipped folder.
4.  Make your changes and test using your project.
5.  Do not worry about documentation or version numbers. Documentation updates
    are nice, but not necessary. Unity Tests are also optional.
6.  Save a copy of your changes to a zipped folder.
7.  Send your zipped folder (and the zipped folder from step 3 if you have one)
    via Unity forums PM, email, or any other contact channel you are aware of.

## Naming

My naming conventions are a little bit different from Unity and more closely
resemble my C++ naming convention. You don’t have to follow these when
developing new features. But expect deviations to be modified in an official
release.

-   All data types, enum values, and methods use PascalCase.
-   All fields and properties use camelCase.
-   Private fields and properties have an “m_” prefix.
-   Internal fields and properties also have an “m_” prefix if they are
    accompanied by public members.
-   ECS tag components have a “Tag” suffix.
-   ECS systems have a “System” suffix.

If you have a tendency to be overly bland or generic with your type, variable,
and function names, please use comments to provide details so that I can better
understand your code.

## My Workflow

I manually synchronize development of the Latios Framework across several
projects. This means I often have multiple versions of the framework with
experimental features. Eventually changes are organized in a release repo which
pushes directly to the official repo on GitHub.

If you would like a copy of any experimental version of the framework,
especially if you wish to develop new features against it and make some of the
existing experimental features release-viable, let me know. But don’t expect
that unreleased experimental stuff to not obliterate your nose with awfulness.

## “I’d love to help, but the stuff you do is way over my head!”

You don’t need to develop new features to help out.

If you use the Latios Framework, you can make demo projects which can help
others understand how to use the API. You can send those projects to me via zip
files or send me a public link to the demo.

If you are an artist or designer, you can create assets and send them to me to
be featured in demos, examples, and tutorials.

## My Single Rule Regarding Examples

Please do not commit anything with LFS. GitHub LFS is expensive and I have not
set up an AWS LFS server or alternative yet. (Any suggestions to make this cheap
will be greatly appreciated!)

If you want LFS, make your own repo and send me the link. I will add it to the
README.
