FROM eclipse-temurin:17-jdk

# Install dependencies
RUN apt-get update && \
    apt-get install -y wget unzip python3 python3-pip && \
    rm -rf /var/lib/apt/lists/*

# Download and extract Apache FOP
RUN wget https://archive.apache.org/dist/xmlgraphics/fop/binaries/fop-2.9-bin.zip && \
    unzip fop-2.9-bin.zip && \
    mv fop-2.9 /opt/fop && \
    rm fop-2.9-bin.zip

# Set working directory
WORKDIR /opt/fop

# Make FOP script executable
RUN chmod +x /opt/fop/fop/fop

# Copy Flask server script
COPY fop-server.py .

# Install Flask
RUN pip3 install --break-system-packages flask

# Expose port
EXPOSE 8080

# Start Flask server
CMD ["python3", "fop-server.py"]
