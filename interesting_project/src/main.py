from src.utils.detector.detector import detect_text_regions
from src.utils.translator.translator import translate_text

def run_pipeline(image_path):
    print(f"Processing image: {image_path}")
    
    # 1. Detect text regions
    regions = detect_text_regions(image_path)
    print(f"Detected {len(regions)} regions.")
    
    # 2. Mock extraction and translation
    for region in regions:
        # Mock extracted text
        original_text = "Hallo Welt"
        translated = translate_text(original_text)
        print(f"Original: {original_text} -> Translation: {translated}")

if __name__ == "__main__":
    run_pipeline("assets/sample_comic.png")
