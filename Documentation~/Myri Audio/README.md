# Myri Audio

Myri Audio is a pure DOTS audio solution designed for handling a myriad of audio
sources. It leverages the C\# job system, the Burst compiler, and the DSPGraph.
At the time of its release initial release (when this page was written), no
official Unity Entities-based audio solution existed.

Check out the [Getting Started](Getting%20Started.md) page!

## Features

### Out-of-the-Box

Myri can play audio without requiring a single line of code. Simply add Myri
Audio Source components to your GameObjects and a Myri Audio Listener Authoring
to your camera transform and you are good to go. A brickwall limiter is applied
to the final audio result so your audio can remain distortion-free no matter how
chaotic your scene is.

### Spatialization

Myri processes sound in 3D space in order to provide the listener a sense of
directionality and immersion. While much simpler than the cutting-edge
techniques used in VR applications, the model is customizable through a listener
profile API. From 2D panning to up to 127 distinct direction channels, Myri can
be tuned to meet your project’s needs.

The default profile produces a similar sound experience to the Megacity demo.

### Voice Combining

Myri can detect when several audio sources are playing the same clip in unison
and combine them to save processing power without losing spatial information.
This feature is in stark contrast to Unity’s FMOD implementation where sources
are omitted when too many are played at once.

The mechanism Myri uses for this is inspired by the Megacity demo. But unlike
the demo, Myri can also combine one-shot sources.

Voice-combining is automatic and requires no effort from the user.

### Cone Emitters

Myri allows audio sources to define cone-shaped emitters. These can be used to
emulate directional audio emitters such as loudspeakers.

Cone emitters are fully modifiable at runtime.

### ECS-Friendly Job Scheduling

Myri has no synchronous dependencies and will not force any jobs to complete
other than those it has scheduled in a previous update.

Most DSPGraph-based solutions schedule jobs from the mixer thread to perform
intensive sampling. These jobs must compete for resources with jobs scheduled
from the main thread, which can lead to frame-time inconsistencies for heavily
jobified projects. Myri does not schedule jobs from the mixer thread. Instead,
Myri schedules sampling jobs to execute during an ECS sync point, utilizing
worker threads that would otherwise be idle. This essentially makes Myri’s
sampling work “free”.

*Warning: While this approach reduces frame times, it requires more computation
effort. Consequently, it may not be well-suited for mobile platforms where
battery life is a concern.*

### Simple, Easy, and Job-friendly

Myri aims to provide a simple, easy, and job-friendly API. Rather than using
shared components or managed components, Myri uses blob assets to store sample
data. You can easily query and assign clips to audio sources inside Bursted
jobs.

Audio sources automatically begin playing as soon as Myri detects them. For
those familiar with classical Unity terminology, audio sources always exhibit
“Play on Awake” behavior. This means playing a one-shot is as simple as
instantiating a prefab. You can even command Myri to automatically destroy the
entity when the clip is done playing.

### Multiple Listeners

Yes. Multiple listeners are supported. Their outputs are automatically mixed
together. I’m probably crazy for supporting this, but if this was a feature you
were hoping for, well I’m glad I delivered.

## Known Issues

-   Myri may drop the first samples of a newly instantiated audio source if the
    audio framerate is faster than the simulation framerate. A configurable
    audio framerate multiplier exists to combat this, but issues may still arise
    during frame spikes.
-   Myri will only use up to `n` worker threads when performing sampling, where
    `n` is the sum of spatialization channels across all listeners. A default
    listener has four spatialization channels.

## Known DSPGraph Issues

The following issues are issues with Unity’s underlying DSPGraph and cannot be
resolved in Myri. If you encounter one of these issues, submit a bug report to
Unity!

-   A job which manages listeners and the DSP graph is not Bursted due to
    DSPGraph limitations.
-   The Unity Editor sometimes emits an exception from a Bursted job. This is a
    DSPGraph and job system bug related to scheduling, and does not appear to
    have any adverse effects currently.
-   Exiting play mode while an active listener exists will cause a warning
    regarding DSPGraph nodes not being disposed. This is an execution order
    issue on teardown.
-   Audio causes crashes for MacOS builds using 2020.1.9f1 and possibly other
    versions. (1 report)
-   DSPGraph does not initialize correctly on 2020.3 using the new build system
    packages for Windows (1 report)

## Near-Term Roadmap

-   Clip compression for long clips in blobs
-   Layers
-   Custom sample generation API
-   DSPGraph post-processing API
-   Pitch shifting
