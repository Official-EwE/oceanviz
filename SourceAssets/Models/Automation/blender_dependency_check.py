import bpy
import os


def find_blend_files(directory):
    blend_files = []
    for root, dirs, files in os.walk(directory):
        for f in files:
            if f.lower().endswith(".blend"):
                blend_files.append(os.path.join(root, f))
    return blend_files


# Set the directory you want to search for .blend files
directory = r"./../"

# Find all blend files in the given directory and its subfolders
blend_files = find_blend_files(directory)

# Dictionary to hold the results
# Key: blend file path
# Value: list of linked libraries
results_libs = {}
results_size = {}

for blend_file in blend_files:
    # check file size, warn if it's over 50MB
    file_size = os.path.getsize(blend_file)
    if file_size > 50 * 1024 * 1024:
        results_size[blend_file] = file_size

    bpy.ops.wm.open_mainfile(filepath=blend_file)
    libraries = bpy.data.libraries

    # resave the file as compressed
    # bpy.ops.wm.save_as_mainfile(filepath=blend_file, compress=True)

    if len(libraries) == 0:
        results_libs[blend_file] = []
    else:
        results_libs[blend_file] = [lib.filepath for lib in libraries]

print("Final Results:")
# Print all results at the end
for blend_file, libs in results_libs.items():
    if libs:
        print(f"Blend File: {blend_file}")
        for lib in libs:
            print(f"    - {lib}")


for blend_file, size in results_size.items():
    print(f"Warning: File {blend_file} is {size / (1024 * 1024):.2f}MB")
