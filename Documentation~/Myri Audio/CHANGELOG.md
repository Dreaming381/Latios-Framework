# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic
Versioning](http://semver.org/spec/v2.0.0.html).

## [0.4.0] – 2021-8-9

Officially supports Entities [0.17.0]

### Added

-   Added AudioSettings.lookaheadAudioFrames which can be used to correct for
    when sampling regularly takes too long to deliver to the audio thread.

### Changed

-   **Breaking:** The concept of “subframes” has been removed. Audio sources now
    synchronize with the actual audio frame definition. In audioSettings,
    audioFramesPerUpdate now represents how many frames are expected to be
    played before the next buffer is consumed, effectively replicating the old
    subframe behavior. A new variable named safetyAudioFrames represents how
    many additional audio frames to sample in each update to prevent starvation.

### Improved

-   Myri is now more robust to AudioSettings changes while clips are playing.
-   *New Feature:* Myri now has a brickwall limiter at the end of its DSP chain
    on the audio thread. The current implementation may consume measurable
    resources on the audio thread and potentially lead to performance
    regressions. However, this limiter removes the distortion artifacts when too
    many loud audio sources play at once. Balancing the volume of audio sources
    is much less important now, and the overall out-of-the-box quality has
    greatly improved.

## [0.3.3] – 2021-6-19

Officially supports Entities [0.17.0]

### Fixed

-   Fixed an issue where looping sources with a sample rate different from the
    output sample rate would begin sampling from the wrong location each update.

## [0.3.2] – 2021-5-25

Officially supports Entities [0.17.0]

### Fixed

-   Renamed some files so that Unity would not complain about them matching the
    names of GameObject components.

## [0.3.0] – 2021-3-4

Officially supports Entities [0.17.0]

### This is the first release of *Myri Audio*.
