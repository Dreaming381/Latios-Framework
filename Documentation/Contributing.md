Contributing
============

I don’t really care about money. I don’t drink coffee either, so I wouldn’t even
know what to do with small donations anyways.

What I do care about is time. So if you write code for me (or commission someone
I suppose), that may be code I don’t have to write, which saves me time. Here’s
how to do it.

Steps
-----

1.  Ask me about new features

    -   Odds are I probably have some design ideas and/or requirements for any
        given new feature. Ask me to get that info before you write something I
        don’t want to take.

    -   If you wish to develop a new feature, try to pick a low priority item,
        unless you can make a fast turnaround and have discussed with me
        timelines.

    -   The best means to reach me the first time is via a PM in the Unity
        forums.

    -   You do not have to ask me about adding tests and examples.

2.  Implement your new code

    -   Try to follow naming conventions.

    -   Don’t worry about formatting. I have a little tool I wrote called Alina
        that automatically formats my code. I don’t give Alina enough love for
        how much I ask of her, and sometimes she gives me stupid-looking code as
        payback. If you want to meet her, I can arrange something. But
        otherwise, don’t worry about the formatting. I’ll ask her to about
        formatting myself.

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

Naming
------

My naming conventions are a little bit different from Unity and more closely
resemble my C++ naming convention.

-   All data types, enum values, and methods use PascalCase.

-   All fields and properties use camelCase.

-   Private fields and properties have an “m_” prefix.

-   ECS tag components have a “Tag” suffix.

-   ECS systems have a “System” suffix.

My Workflow
-----------

My actual main development is all done in a private repository called
Reflections OP, which is the name of a game I prototyped a while back in a game
jam and had been working on until scope creep convinced me to focus on building
this framework with small game jam games until I was ready to tackle that larger
scope project again.

Anyways, this repository contains not just the code I wish to release, but also
a bunch of experimental stuff I am not ready to release yet. Once I reach a
point where I want to create a release, I migrate the files I wish to release to
the public repo.

If you would like a copy of the private repo, especially if you wish to develop
new features against it and make some of the existing experimental features
release-viable, let me know. But don’t expect that unreleased experimental stuff
to not obliterate your nose with awfulness.

“I’d love to help, but the stuff you do is way over my head!”
-------------------------------------------------------------

My suggestion is to write tests and examples. I don’t personally write many
tests for this framework because this is a hobby project and I have chosen not
to spend my time writing tests and am willing to deal with the consequences. But
if you write tests, I will use them, and it will be a great way for you to learn
how this stuff works while simultaneously protecting my present self from my
future self (or vice versa). Examples are also pretty cool and can help people
new to the framework get understand what is going on.

My Single Rule Regarding Examples
---------------------------------

Please do not commit anything with LFS. GitHub LFS is expensive and I have not
set up an AWS LFS server yet.
