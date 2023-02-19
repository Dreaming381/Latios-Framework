# Contributing

Oh hi! Awesome of you to stop by!

No. I don’t want your money. And no, I don’t drink coffee.

Writing code is where it’s at! Whether you want to, your boss is telling you to,
or you are paying someone to, your contributions will not go unnoticed!

Here’s how to contribute.

## Picking a Feature

If you are just looking to contribute some bug fixes, skip this step.

If you already have a feature in mind, be sure to run it by me via various
channels. The Discord server is usually the best (public or DMs) if you want
fast responses. But forum PMs, GitHub discussions, and email all work as well.

If you don’t have a feature in mind, worry not. There are plenty of open
challenges to solve. Not all of them require the brainpower of a super-genius
either. Convenience utilities, Editor tooling with UI Toolkit, and dev-ops are
all areas that could really use your help. And someone needs to make the example
projects.

If you a looking for an optimization challenge, there’s plenty around too, many
of which will have real impacts for lots of users.

Always ask for the latest open challenges via Discord, forums, GitHub, or email.

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
10. PRs will land in a staging branch. Pay attention to reviews. Some may have
    suggested improvements. However, if such suggestions are trivial, they might
    be made in the next release commit on top of your commits’ changes.

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
    via Discord, Unity forums PM, email, or any other contact channel you are
    aware of. The zipped folder should typically be under 2 MB in size (exclude
    documentation in your zip).

Your changes will be cherry-picked into a local development project and included
in the release. You will not be included in git metadata associated with the
changes. I use tools like Meld for merging such changes, in case you were
wondering.

## Naming

My naming conventions are a little bit different from Unity and more closely
resemble my C++ naming convention. You don’t have to follow these when
developing new features. But expect deviations to be modified in an official
release.

-   All data types, enum values, and methods use PascalCase.
-   All fields and properties use camelCase.
-   Private fields and properties have an “m_” prefix.
-   Internal fields and properties may also have an “m_” prefix if they are
    accompanied by public members.
-   ECS tag components have a “Tag” suffix.
-   Zero-size enableable components have a “Flag” suffix.
-   ECS systems have a “System” suffix.

If you have a tendency to be overly bland or generic with your type, variable,
and function names, please use comments to provide details so that I can better
understand your code.

## Formatting

In general, do not worry about formatting. Instead, format the files you intend
to modify first, and then make your changes such that I can see the diff of just
the changes and not your formatting.

I have my own tool for formatting which I use for personal development called
“Alina”. It is not perfect, so if you are interested in helping it be better and
usable by more people, let me know!

## Tests

If you want them, write them. But in general, my philosophy has always been to
test things with real projects. That way, I not only test the code itself, but
also the practicality of such APIs. If you don’t like testing things, don’t
worry about it.

## The Final Step

Let me know what name you would like to be referred to in the Contributors
section of the README. If you don’t have a preference, I will typically default
to your Unity forums username. If you have a webpage you would like to be linked
to your name, let me know that as well.

## My Own Workflow

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

Also, a huge help for future users is writing devlogs about your adventures with
the Latios Framework. You can even showcase your progress in the Discord! We
love it!
