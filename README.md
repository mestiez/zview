# zview

Image viewer for X11 heavily inspired by [NoBS Image Viewer](https://ropemikad.itch.io/nobs-image-viewer).

Supports `avif`, `tga`, `tiff`, `webp`, `bmp`, `gif`, `ico`, `jpeg`, `png`, and `qoi`. 

## Installation

The easiest way is by using Cargo, which can be installed using [rustup](https://rustup.rs/).

Run `cargo install --git https://github.com/mestiez/zview`

## Dependencies

- libsdl3

## Building

Ensure libsdl3 and Cargo are installed.

Run `cargo build -r` in the project folder. The output binary will be at `target/release/zview`. You can move it to your local binary path.

Or use the [prebuilt binaries](https://github.com/mestiez/zview/releases) if you're on amd64.

## Usage

    zview [options] [path]

	Arguments:
    path              Path to the image or directory (optional).

    Options:
    -v, --version     Display version information.
    -h, --help        Show help and usage information.

You can also drag the file into the window.
Touchscreen pan and pinch zoom are supported.

|         Input | Action                  |
|--------------:|:------------------------|
|         `LMB` | Pan                     |
|           `R` | Rotate clockwise        |
|     `Shift+R` | Rotate counter-clockwise|
|           `V` | Flip (vertical)         |
|           `H` | Flip (horizontal)       |
|        `Home` | Reset camera            |
|           `.` | Auto fit image          |
|           `W` | Auto size window        |
|           `B` | Toggle background       |
|           `F` | Toggle linear filtering |
|           `T` | Increment tiling        |
|     `Shift+T` | Decrement tiling        |
|      `Ctrl+V` | Load from clipboard     |
|      `Ctrl+C` | Copy to clipboard       |
|          `->` | Next in directory       |
|          `<-` | Previous in directory   |
|          `F5` | Reload image            |
|           `I` | Show image info         |
| `Q`, `Escape` | Quit                    |

## Known issues and limitations

- All images are fully loaded into memory before being displayed. For large and/or animated images, this can become a
  problem. 
- HDR images aren't rendered correctly
- Some colorspace information is ignored
- "Copy to clipboard" isn't working reliably
- The directory queue is reloaded every time an arrow key is pressed (should probably be cached until F5 is pressed)
