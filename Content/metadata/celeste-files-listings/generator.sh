echo "Enter the celeste install path: "
read -r CELESTE_PATH
read -rp "Install type (L|Linux / M|Mac / W|Windows): " TYPE

OUT_FILE="celeste_files_"

case $TYPE in
    L | Linux)
        OUT_FILE+="linux.yaml"
        ;;
    M | Mac)
        OUT_FILE+="mac.yaml"
        ;;
    W | Windows)
        OUT_FILE+="windows.yaml"
        ;;
    *)
        echo "Unknown OS. Exiting..."
        exit
        ;;
esac

if [ -f "./$OUT_FILE" ]; then
    rm "./$OUT_FILE"
fi

for FILE in "$CELESTE_PATH"/*; 
    do [ -f "$FILE" ] && echo "- $(realpath --relative-to="$CELESTE_PATH" "$FILE")" >> ./"$OUT_FILE";
done;

    
    