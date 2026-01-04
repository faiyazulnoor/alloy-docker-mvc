from flask import Flask, request, send_file, jsonify
import subprocess
import tempfile
import os

app = Flask(__name__)

# Path to the FOP binary inside the container
FOP_PATH = "/opt/fop/fop/fop"  # Note the double /fop

@app.route("/")
def index():
    return "FOP server is running! Use POST /convert to upload FO files."

@app.route("/convert", methods=["POST"])
def convert_fo():
    if 'file' not in request.files:
        return jsonify({"error": "No file uploaded"}), 400

    fo_file = request.files['file']
    
    if fo_file.filename == '':
        return jsonify({"error": "Empty filename"}), 400

    # Save uploaded FO file temporarily
    fo_temp = tempfile.NamedTemporaryFile(delete=False, suffix=".fo")
    pdf_temp = tempfile.NamedTemporaryFile(delete=False, suffix=".pdf")
    pdf_temp.close()
    
    try:
        fo_file.save(fo_temp.name)
        fo_temp.close()

        # Run Apache FOP with bash explicitly
        result = subprocess.run(
            ["/bin/bash", FOP_PATH, "-fo", fo_temp.name, "-pdf", pdf_temp.name],
            capture_output=True,
            text=True
        )
        
        if result.returncode != 0:
            error_msg = result.stderr if result.stderr else result.stdout
            return jsonify({
                "error": "FOP conversion failed",
                "returncode": result.returncode,
                "stderr": error_msg,
                "stdout": result.stdout
            }), 500

        # Check if PDF was created
        if not os.path.exists(pdf_temp.name) or os.path.getsize(pdf_temp.name) == 0:
            return jsonify({"error": "PDF file was not created or is empty"}), 500

        # Return PDF
        return send_file(pdf_temp.name, mimetype="application/pdf", as_attachment=True, download_name="output.pdf")

    except Exception as e:
        return jsonify({"error": "Unexpected error", "details": str(e)}), 500
    
    finally:
        # Cleanup temporary FO file
        if os.path.exists(fo_temp.name):
            os.unlink(fo_temp.name)

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8080, debug=True)