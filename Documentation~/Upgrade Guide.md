# Upgrade Guide [0.4.5] [0.5.0]

Please back up your project in a separate directory before upgrading! You will
likely need to reference the pre-upgraded project during the upgrade process.

## Core

### New Bootstrap and Installers

You will likely need to recreate your bootstrap, as the paradigm has completely
changed.

### Scene Manager

Scene Manager no longer runs by default in the standard bootstraps. If you wish
to continue using it, you must install it by modifying the bootstrap and adding
the line to install it.

### Initialization System Order

This changed a bit. If you were relying on specific ordering, you might
encounter a few issues. However, such issues aren’t expected to be common.

## Psyshock Physics

### normalOnSweep -\> normalOnCaster

This rename was made to because the old name was a mistake. It is the only
rename in Psyshock.

## Myri Audio

### IldProfileBlob -\> ListenerProfileBlob

This rename makes the function of the blob more apparent. It is the only rename
in Myri.

## New Things to Try!

### Core

Smart Blobbers are a new way to convert blob assets, and they provide a powerful
workflow for highly general-purpose blob assets

### Psyshock Physics

Convex colliders are new. Try them out!

There’s also new experimental triangle colliders.

### Kinemation

Oh, hey! A new module!
