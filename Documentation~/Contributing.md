# Contributing

Oh hi! Awesome of you to stop by!

No. I don’t want your money. And no, I don’t drink coffee.

But if you want to write code (or commission someone I suppose), that might be
code I don’t have to write. That means more time for me to invent stuff. I like
this plan!

Here’s how to contribute new features.

## Steps

1.  Ask me about new features

    -   Odds are I probably have some design ideas and/or requirements for any
        given new feature. Ask me to get that info before you write something I
        don’t want to accept.

    -   If you wish to develop a new feature, try to pick a low priority item,
        unless you can make a fast turnaround and have discussed timelines with
        me.

    -   The best means to reach me the first time is via a PM in the Unity
        forums.

    -   You do not have to ask me about adding tests and examples. Go for it!

2.  Implement your new code

    -   Try to follow naming conventions.

    -   Don’t worry about formatting. I have a little tool I wrote called Alina
        that automatically formats my code. I don’t give Alina enough love for
        how much I ask of her, and sometimes she gives me stupid-looking code as
        payback. If you want to meet her, I can arrange something. But
        otherwise, don’t worry about the formatting. I’ll ask her to format your
        code for you.

    -   You do not have to write tests. I don’t. However, you are certainly
        welcome to if you wish to. I will keep them (and probably even use
        them).

3.  Make a pull request

    -   If I make a comment requesting changes, I will not do anything with the
        code until you make changes or you ignore me for too long.

    -   If I don’t pull the pull request but mention that I accepted it, that
        means that I am manually copying the changes into the true source
        project where these packages originate and making sure things tie
        together correctly.

    -   Unless your code is somehow absolutely perfectly written and aligned to
        everything that I wish to immediately release it as is, I will not pull
        the pull request but instead close it.

## Naming

My naming conventions are a little bit different from Unity and more closely
resemble my C++ naming convention.

-   All data types, enum values, and methods use PascalCase.

-   All fields and properties use camelCase.

-   Private fields and properties have an “m_” prefix.

-   ECS tag components have a “Tag” suffix.

-   ECS systems have a “System” suffix.

## My Workflow

My main development used to be done in a private repository called Reflections
OP, which is the name of a game I prototyped during a game jam and had been
working on. Then scope creep convinced me to focus on building this framework
with small game jam games until I was ready to tackle that larger scope project
again.

Then Unity fixed the workflow for working with packages in development, and my
active development shifted into the lsss-wip repo which is public and used to
stabilize version 0.2.0. Where my active development takes place will probably
shift around some more depending on what I am working on. I may have
experimental stuff spread out across several repos (there’s some experimental
stuff whose only home is still the Reflections OP repo).

If you would like a copy of any experimental version of the framework,
especially if you wish to develop new features against it and make some of the
existing experimental features release-viable, let me know. But don’t expect
that unreleased experimental stuff to not obliterate your nose with awfulness.

## “I’d love to help, but the stuff you do is way over my head!”

My suggestion is to write tests and examples. I don’t personally write many
tests for this framework because this is a hobby project and I have chosen not
to spend my time writing tests. I am willing to deal with the consequences. But
if you write tests, I will use them, and it will be a great way for you to learn
how this stuff works while simultaneously protecting my present self from my
future self (or vice versa). Examples are also pretty cool and can help people
new to the framework.

## My Single Rule Regarding Examples

Please do not commit anything with LFS. GitHub LFS is expensive and I have not
set up an AWS LFS server or alternative yet. (Any suggestions to make this cheap
will be greatly appreciated!)

If you want LFS, make your own repo and send me the link. I will add it to the
README.
